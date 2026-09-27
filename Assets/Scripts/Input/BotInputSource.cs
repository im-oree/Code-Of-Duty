using System.Collections.Generic;
using UnityEngine;

namespace CodeOfDuty.Input
{
    /// <summary>
    /// Bot intent, driven by an AI behaviour script. This is the entire seam that makes a bot
    /// indistinguishable from a player: it feeds the SAME <see cref="IInputSource"/> contract a
    /// human does, so movement, weapon handling and character state never know the difference.
    ///
    /// A future, stronger AI agent only has to write into these setters — nothing else changes.
    /// </summary>
    public class BotInputSource : IInputSource
    {
        public Vector2 Move { get; set; }
        public Vector2 Look { get; set; }
        public float Lean { get; set; }

        readonly HashSet<InputActionId> held = new HashSet<InputActionId>();
        readonly HashSet<InputActionId> tapped = new HashSet<InputActionId>();
        readonly HashSet<InputActionId> pressedThisFrame = new HashSet<InputActionId>();
        readonly HashSet<InputActionId> releasedThisFrame = new HashSet<InputActionId>();

        /// <summary>Set an action's held state. Enables press/release edges automatically.</summary>
        public void SetHeld(InputActionId action, bool value)
        {
            bool was = held.Contains(action);
            if (value && !was)
            {
                held.Add(action);
                pressedThisFrame.Add(action);
            }
            else if (!value && was)
            {
                held.Remove(action);
                releasedThisFrame.Add(action);
            }
        }

        /// <summary>
        /// Fire a one-frame tap, for actions the AI does not hold down — jump, reload, swap.
        ///
        /// The tap is automatically released on the next tick. Holding it instead would be a
        /// nasty class of bug: a bot that tapped Jump once would read as holding Jump forever,
        /// and a bot that tapped Crouch would never stand up again.
        /// </summary>
        public void Press(InputActionId action)
        {
            tapped.Add(action);
            pressedThisFrame.Add(action);
        }

        public bool Held(InputActionId action) => held.Contains(action) || tapped.Contains(action);
        public bool Pressed(InputActionId action) => pressedThisFrame.Contains(action);
        public bool Released(InputActionId action) => releasedThisFrame.Contains(action);

        /// <summary>
        /// Retires the previous frame's edges. Called by <see cref="CharacterInput"/> at
        /// execution order -100, before any AI writes and long before any consumer reads, so a
        /// value written by a brain this frame is guaranteed to survive until it is consumed.
        /// </summary>
        public void Tick(float deltaTime)
        {
            pressedThisFrame.Clear();
            releasedThisFrame.Clear();

            // A tap lasts exactly one frame, then reports a clean release edge.
            foreach (var action in tapped) releasedThisFrame.Add(action);
            tapped.Clear();
        }
    }
}
