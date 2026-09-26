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

        readonly HashSet<InputActionId> held = new HashSet<InputActionId>();
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

        /// <summary>Fire a one-frame press (for taps the AI does not hold).</summary>
        public void Press(InputActionId action)
        {
            held.Add(action);
            pressedThisFrame.Add(action);
        }

        public bool Held(InputActionId action) => held.Contains(action);
        public bool Pressed(InputActionId action) => pressedThisFrame.Contains(action);
        public bool Released(InputActionId action) => releasedThisFrame.Contains(action);

        /// <summary>Call once per frame BEFORE reading, to clear edge state from the previous frame.</summary>
        public void Tick(float deltaTime)
        {
            pressedThisFrame.Clear();
            releasedThisFrame.Clear();
        }
    }
}
