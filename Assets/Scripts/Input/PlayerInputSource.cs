using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace CodeOfDuty.Input
{
    /// <summary>
    /// Keyboard + mouse + gamepad implementation of <see cref="IInputSource"/> built on the
    /// Unity Input System devices. Self-contained: it never touches the legacy Input class,
    /// so it works on any platform with any controller without an .inputactions asset.
    ///
    /// Keyboard bindings are rebindable and persist in PlayerPrefs; the gamepad mapping is a
    /// standard shooter layout (movement = left stick, look = right stick).
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInputSource : MonoBehaviour, IInputSource
    {
        [Header("Mouse")]
        [Tooltip("Base mouse look multiplier. Scaled by GameSettings.MouseSensitivity at runtime.")]
        [SerializeField] private float mouseSensitivity = 0.08f;

        [Header("Gamepad")]
        [SerializeField] private float gamepadLookSensitivity = 2.4f;
        [Range(0f, 0.5f)]
        [SerializeField] private float gamepadLookDeadzone = 0.16f;
        [SerializeField] private bool invertY;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }

        readonly HashSet<InputActionId> held = new HashSet<InputActionId>();
        readonly HashSet<InputActionId> pressed = new HashSet<InputActionId>();
        readonly HashSet<InputActionId> released = new HashSet<InputActionId>();

        static readonly InputActionId[] AllActions =
            (InputActionId[])System.Enum.GetValues(typeof(InputActionId));

        void Update() => Poll();

        void Poll()
        {
            held.Clear();
            pressed.Clear();
            released.Clear();

            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;

            ReadMovement(keyboard, gamepad);
            ReadLook(gamepad);
            ReadButtons(keyboard, gamepad);
        }

        void ReadMovement(Keyboard keyboard, Gamepad gamepad)
        {
            float x = 0f, y = 0f;
            if (keyboard != null)
            {
                if (keyboard[Key.A].isPressed) x -= 1f;
                if (keyboard[Key.D].isPressed) x += 1f;
                if (keyboard[Key.S].isPressed) y -= 1f;
                if (keyboard[Key.W].isPressed) y += 1f;
            }
            if (gamepad != null)
            {
                Vector2 stick = ApplyDeadzone(gamepad.leftStick.ReadValue(), gamepadLookDeadzone);
                x += stick.x;
                y += stick.y;
            }
            Move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        void ReadLook(Gamepad gamepad)
        {
            Vector2 look = Vector2.zero;

            var mouse = Mouse.current;
            if (mouse != null)
                look += mouse.delta.ReadValue() * (mouseSensitivity * GameSettings.MouseSensitivity);

            if (gamepad != null)
            {
                Vector2 stick = ApplyDeadzone(gamepad.rightStick.ReadValue(), gamepadLookDeadzone);
                // frame-rate independent-ish: stick is a rate, not a delta
                look += stick * (gamepadLookSensitivity * Time.deltaTime * 60f);
            }

            if (invertY) look.y = -look.y;
            Look = look;
        }

        void ReadButtons(Keyboard keyboard, Gamepad gamepad)
        {
            // mouse buttons (Fire/Aim) have no Key equivalent
            var mouse = Mouse.current;
            Accumulate(InputActionId.Fire, mouse != null && mouse.leftButton.isPressed,
                mouse != null && mouse.leftButton.wasPressedThisFrame,
                mouse != null && mouse.leftButton.wasReleasedThisFrame);

            Accumulate(InputActionId.Aim, mouse != null && mouse.rightButton.isPressed,
                mouse != null && mouse.rightButton.wasPressedThisFrame,
                mouse != null && mouse.rightButton.wasReleasedThisFrame);

            foreach (var action in AllActions)
            {
                bool isHeld = false, isPressed = false, isReleased = false;

                // keyboard
                Key key = InputMap.GetKey(action);
                if (keyboard != null && key != Key.None)
                {
                    var control = keyboard[key];
                    isHeld |= control.isPressed;
                    isPressed |= control.wasPressedThisFrame;
                    isReleased |= control.wasReleasedThisFrame;
                }

                // gamepad
                var button = GamepadButton(action, gamepad);
                if (button != null)
                {
                    isHeld |= button.isPressed;
                    isPressed |= button.wasPressedThisFrame;
                    isReleased |= button.wasReleasedThisFrame;
                }

                // sprint is also "hold left stick" on pad (already covered), and
                // next/prev weapon fall back to the mouse wheel
                if (action == InputActionId.NextWeapon && mouse != null && mouse.scroll.ReadValue().y > 0.01f)
                    isPressed = true;
                if (action == InputActionId.PrevWeapon && mouse != null && mouse.scroll.ReadValue().y < -0.01f)
                    isPressed = true;

                Accumulate(action, isHeld, isPressed, isReleased);
            }
        }

        void Accumulate(InputActionId action, bool isHeld, bool isPressed, bool isReleased)
        {
            if (isHeld) held.Add(action);
            if (isPressed) pressed.Add(action);
            if (isReleased) released.Add(action);
        }

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
                case InputActionId.NextWeapon: return g.rightShoulder;
                case InputActionId.PrevWeapon: return g.leftShoulder;
                case InputActionId.Pause: return g.startButton;
                case InputActionId.Scoreboard: return g.selectButton;
                default: return null;
            }
        }

        static Vector2 ApplyDeadzone(Vector2 raw, float deadzone)
        {
            float magnitude = raw.magnitude;
            if (magnitude < deadzone) return Vector2.zero;
            // rescale so movement starts from 0 at the deadzone edge
            float scaled = (magnitude - deadzone) / (1f - deadzone);
            return raw.normalized * Mathf.Clamp01(scaled);
        }

        public bool Held(InputActionId action) => held.Contains(action);
        public bool Pressed(InputActionId action) => pressed.Contains(action);
        public bool Released(InputActionId action) => released.Contains(action);

        public void Tick(float deltaTime) { /* device polling happens in Update */ }
    }

    /// <summary>
    /// Keyboard bindings for the Input System, rebindable and persisted. Gamepad uses a fixed
    /// standard layout. Mirrors the role of the legacy <c>InputBindings</c> registry while the
    /// project migrates over.
    /// </summary>
    public static class InputMap
    {
        static readonly Dictionary<InputActionId, Key> defaults = new Dictionary<InputActionId, Key>
        {
            { InputActionId.Jump, Key.Space },
            { InputActionId.Crouch, Key.C },
            { InputActionId.Sprint, Key.LeftShift },
            { InputActionId.Fire, Key.None },      // mouse left
            { InputActionId.Aim, Key.None },       // mouse right
            { InputActionId.Reload, Key.R },
            { InputActionId.Melee, Key.V },
            { InputActionId.Inspect, Key.F },
            { InputActionId.Interact, Key.E },
            { InputActionId.Lethal, Key.Q },
            { InputActionId.Tactical, Key.G },
            { InputActionId.SlotPrimary, Key.Digit1 },
            { InputActionId.SlotSecondary, Key.Digit2 },
            { InputActionId.SlotMelee, Key.Digit3 },
            { InputActionId.NextWeapon, Key.None },   // scroll up
            { InputActionId.PrevWeapon, Key.None },   // scroll down
            { InputActionId.Streak1, Key.Z },
            { InputActionId.Streak2, Key.X },
            { InputActionId.Streak3, Key.B },
            { InputActionId.ToggleView, Key.P },
            { InputActionId.Scoreboard, Key.Tab },
            { InputActionId.Pause, Key.Escape },
        };

        public static Key GetKey(InputActionId action)
        {
            if (!defaults.TryGetValue(action, out Key fallback)) return Key.None;
            int stored = PlayerPrefs.GetInt("iskey_" + action, (int)fallback);
            return (Key)stored;
        }

        public static void SetKey(InputActionId action, Key key)
        {
            PlayerPrefs.SetInt("iskey_" + action, (int)key);
        }

        public static void ResetToDefaults()
        {
            foreach (var pair in defaults)
                PlayerPrefs.DeleteKey("iskey_" + pair.Key);
        }

        public static IEnumerable<KeyValuePair<InputActionId, Key>> Current()
        {
            foreach (var pair in defaults)
                yield return new KeyValuePair<InputActionId, Key>(pair.Key, GetKey(pair.Key));
        }
    }
}
