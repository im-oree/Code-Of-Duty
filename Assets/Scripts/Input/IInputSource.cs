using UnityEngine;

namespace CodeOfDuty.Input
{
    /// <summary>
    /// Every logical gameplay action. Nothing downstream of <see cref="IInputSource"/>
    /// may read a device directly — this enum is the whole vocabulary.
    /// </summary>
    public enum InputActionId
    {
        // movement
        Jump,
        Crouch,
        Sprint,

        // combat
        Fire,
        Aim,
        Reload,
        Melee,
        Inspect,

        // equipment
        Interact,
        Lethal,
        Tactical,

        // inventory
        SlotPrimary,
        SlotSecondary,
        SlotMelee,
        NextWeapon,
        PrevWeapon,

        // scorestreaks
        Streak1,
        Streak2,
        Streak3,

        // ui / misc
        ToggleView,
        Scoreboard,
        Pause
    }

    /// <summary>
    /// A source of player intent. Implemented by real players
    /// (<c>PlayerInputSource</c>) and by bots (<c>BotInputSource</c>) so both drive the
    /// exact same movement, weapon and character-state code with no branch anywhere.
    /// </summary>
    public interface IInputSource
    {
        /// <summary>Movement intent, x = strafe, y = forward. Magnitude is clamped to 1 by callers.</summary>
        Vector2 Move { get; }

        /// <summary>Look delta for this frame, in device-independent units.</summary>
        Vector2 Look { get; }

        bool Held(InputActionId action);
        bool Pressed(InputActionId action);
        bool Released(InputActionId action);

        /// <summary>Called exactly once per frame by the owner so edge state stays coherent.</summary>
        void Tick(float deltaTime);
    }
}
