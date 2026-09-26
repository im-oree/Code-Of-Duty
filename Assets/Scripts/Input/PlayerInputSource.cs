using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CodeOfDuty.Input
{
    /// <summary>
    /// The only class in the game that reads an input device.
    ///
    /// Everything downstream — movement, weapons, character state, camera — talks to
    /// <see cref="IInputSource"/> instead, which is what lets a bot drive an identical body
    /// through an identical code path.
    ///
    /// Two devices, deliberately handled by two different APIs:
    ///
    ///   - <b>Keyboard and mouse</b> go through <see cref="InputBindings"/>, the project's
    ///     keybinding registry. That registry already owns the defaults, the PlayerPrefs
    ///     persistence and the rebinding UI, so routing through it means a key the player
    ///     remaps in Settings takes effect here with no second registry to keep in sync. An
    ///     earlier version of this file shipped its own parallel binding table; two sources of
    ///     truth for "what key is Jump" is a bug waiting to happen, so there is now one.
    ///   - <b>Gamepad</b> goes through the Input System, which handles hotplug, triggers as
    ///     analogue axes, and every pad layout without a per-device table.
    ///
    /// Mixing the two APIs is supported by the project (`activeInputHandler: 2`, meaning both
    /// backends are enabled) and is the pragmatic choice: it adds controller support without
    /// throwing away a working, already-wired rebinding UI.
    ///
    /// Look values are produced in the same units the camera has always received — a rate that
    /// the camera multiplies by delta time — so adding pad support does not change mouse feel.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInputSource : MonoBehaviour, IInputSource
    {
        [Header("Gamepad look")]
        [Tooltip("Multiplier on the shared look sensitivity when using a stick. "
               + "1.5 puts full deflection at roughly 220 deg/s, the COD hip-fire default.")]
        [SerializeField] private float gamepadLookSensitivity = 1.5f;

        [Tooltip("Radial inner deadzone. Below this the stick reads as centred.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float stickInnerDeadzone = 0.08f;

        [Tooltip("Radial outer deadzone. At or above this the stick reads as fully deflected.")]
        [Range(0.5f, 1f)]
        [SerializeField] private float stickOuterDeadzone = 0.95f;

        [Tooltip("Response curve exponent for look. Above 1 gives finer control near centre.")]
        [SerializeField] private float lookResponseExponent = 1.8f;

        [SerializeField] private bool invertY;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public float Lean { get; private set; }

        readonly HashSet<InputActionId> held = new HashSet<InputActionId>();
        readonly HashSet<InputActionId> pressed = new HashSet<InputActionId>();
        readonly HashSet<InputActionId> released = new HashSet<InputActionId>();

        static readonly InputActionId[] AllActions =
            (InputActionId[])System.Enum.GetValues(typeof(InputActionId));

        /// <summary>
        /// Sample every device for this frame.
        ///
        /// Called by <see cref="CharacterInput"/> rather than from this component's own Update,
        /// so polling is guaranteed to happen once, before any consumer reads. Relying on
        /// Unity's default script order here would surface as a one-frame input delay.
        /// </summary>
        public void Tick(float deltaTime)
        {
            held.Clear();
            pressed.Clear();
            released.Clear();

            var gamepad = Gamepad.current;
            ReadMovement(gamepad);
            ReadLook(gamepad);
            ReadLean(gamepad);
            ReadButtons(gamepad);
        }

        void ReadMovement(Gamepad gamepad)
        {
            float x = 0f, y = 0f;

            // Legacy axes keep WASD working alongside whatever the project's InputManager
            // already defines, including any existing arrow-key or alternate bindings.
            x += UnityEngine.Input.GetAxisRaw("Horizontal");
            y += UnityEngine.Input.GetAxisRaw("Vertical");

            if (gamepad != null)
            {
                Vector2 stick = ApplyRadialDeadzone(gamepad.leftStick.ReadValue());
                x += stick.x;
                y += stick.y;
            }

            Move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        void ReadLook(Gamepad gamepad)
        {
            // Mouse: the legacy smoothed axes, unchanged, so feel is identical to before.
            Vector2 look = new Vector2(
                UnityEngine.Input.GetAxis("Mouse X"),
                UnityEngine.Input.GetAxis("Mouse Y"));

            if (gamepad != null)
            {
                Vector2 stick = ApplyRadialDeadzone(gamepad.rightStick.ReadValue());
                // A stick is a rate, not a delta: shaping its magnitude gives fine control
                // near centre without capping the top speed.
                float magnitude = Mathf.Pow(stick.magnitude, lookResponseExponent);
                look += stick.normalized * (magnitude * gamepadLookSensitivity);
            }

            if (invertY) look.y = -look.y;
            Look = look;
        }

        void ReadLean(Gamepad gamepad)
        {
            float lean = UnityEngine.Input.GetAxisRaw("Slope");
            if (gamepad != null)
            {
                // Shoulder buttons lean; they are not otherwise bound on a shooter layout.
                if (gamepad.leftShoulder.isPressed) lean -= 1f;
                if (gamepad.rightShoulder.isPressed) lean += 1f;
            }
            Lean = Mathf.Clamp(lean, -1f, 1f);
        }

        void ReadButtons(Gamepad gamepad)
        {
            foreach (var action in AllActions)
            {
                bool isHeld = false, isPressed = false, isReleased = false;

                // Keyboard and mouse, through the rebindable registry.
                string binding = BindingIdFor(action);
                if (binding != null)
                {
                    isHeld |= InputBindings.Held(binding);
                    isPressed |= InputBindings.Down(binding);
                    isReleased |= InputBindings.Up(binding);
                }

                var button = GamepadButton(action, gamepad);
                if (button != null)
                {
                    isHeld |= button.isPressed;
                    isPressed |= button.wasPressedThisFrame;
                    isReleased |= button.wasReleasedThisFrame;
                }

                // Weapon cycling has no key by default; the wheel is the expected gesture.
                float scroll = UnityEngine.Input.mouseScrollDelta.y;
                if (action == InputActionId.NextWeapon && scroll > 0.01f) isPressed = true;
                if (action == InputActionId.PrevWeapon && scroll < -0.01f) isPressed = true;

                if (isHeld) held.Add(action);
                if (isPressed) pressed.Add(action);
                if (isReleased) released.Add(action);
            }
        }

        /// <summary>
        /// Map a logical action onto its entry in the keybinding registry.
        ///
        /// Null means "no keyboard binding" — the action is pad-only or gesture-only. Keeping
        /// this mapping explicit, rather than deriving it from the enum name, means renaming an
        /// action cannot silently unbind it.
        /// </summary>
        static string BindingIdFor(InputActionId action)
        {
            switch (action)
            {
                case InputActionId.Jump: return "jump";
                case InputActionId.Crouch: return "crouch";
                case InputActionId.Sprint: return "sprint";
                case InputActionId.Fire: return "fire";
                case InputActionId.Aim: return "aim";
                case InputActionId.Reload: return "reload";
                case InputActionId.SwitchSight: return "sightSwitch";
                case InputActionId.Interact: return "interact";
                case InputActionId.Lethal: return "grenade";
                case InputActionId.Tactical: return "tactical";
                case InputActionId.SlotPrimary: return "weapon1";
                case InputActionId.SlotSecondary: return "weapon2";
                case InputActionId.SlotMelee: return "melee";
                case InputActionId.Melee: return "meleeAttack";
                case InputActionId.Inspect: return "inspect";
                case InputActionId.ToggleView: return "viewToggle";
                case InputActionId.Scoreboard: return "scoreboard";
                case InputActionId.Pause: return "pause";
                case InputActionId.Streak1: return "streak1";
                case InputActionId.Streak2: return "streak2";
                case InputActionId.Streak3: return "streak3";
                default: return null;   // NextWeapon / PrevWeapon: mouse wheel
            }
        }

        /// <summary>Standard console shooter layout. Fixed, because pad users expect it fixed.</summary>
        static ButtonControl GamepadButton(InputActionId action, Gamepad g)
        {
            if (g == null) return null;
            switch (action)
            {
                case InputActionId.Jump: return g.buttonSouth;
                case InputActionId.Crouch: return g.buttonEast;
                case InputActionId.Sprint: return g.leftStickButton;
                case InputActionId.Melee: return g.rightStickButton;
                case InputActionId.Reload: return g.buttonWest;
                case InputActionId.Interact: return g.buttonNorth;
                case InputActionId.Fire: return g.rightTrigger;
                case InputActionId.Aim: return g.leftTrigger;
                case InputActionId.Lethal: return g.dpad.up;
                case InputActionId.Tactical: return g.dpad.down;
                case InputActionId.SlotPrimary: return g.dpad.left;
                case InputActionId.SlotSecondary: return g.dpad.right;
                case InputActionId.Pause: return g.startButton;
                case InputActionId.Scoreboard: return g.selectButton;
                default: return null;
            }
        }

        /// <summary>
        /// Radial deadzone with an outer edge, rescaled so travel starts at zero just past the
        /// inner edge and reaches exactly one at the outer edge. Radial rather than per-axis, so
        /// a diagonal push is not favoured over a straight one.
        /// </summary>
        Vector2 ApplyRadialDeadzone(Vector2 raw)
        {
            float magnitude = raw.magnitude;
            if (magnitude < stickInnerDeadzone) return Vector2.zero;
            float scaled = (magnitude - stickInnerDeadzone)
                         / Mathf.Max(0.0001f, stickOuterDeadzone - stickInnerDeadzone);
            return raw.normalized * Mathf.Clamp01(scaled);
        }

        public bool Held(InputActionId action) => held.Contains(action);
        public bool Pressed(InputActionId action) => pressed.Contains(action);
        public bool Released(InputActionId action) => released.Contains(action);
    }
}
