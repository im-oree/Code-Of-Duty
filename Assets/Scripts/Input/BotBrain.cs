using UnityEngine;

namespace CodeOfDuty.Input
{
    /// <summary>
    /// Base class for anything that drives a bot.
    ///
    /// Exists to pin one thing that is otherwise invisible and painful to debug: <b>when</b> an
    /// AI is allowed to write its intent. The frame runs
    ///
    ///   -100  <see cref="CharacterInput"/>  retires last frame's press/release edges
    ///    -90  this class                    writes this frame's intent
    ///      0  movement, weapons, camera     read that intent
    ///
    /// Write outside that window and the input is either wiped before anyone sees it or applied
    /// a frame late — which looks like "the bots feel sluggish" rather than like a bug.
    ///
    /// Subclasses implement <see cref="Think"/> and drive <see cref="Bot"/>. Everything they can
    /// express is exactly what a player can express, so a bot cannot cheat by construction: it
    /// has no way to set a position, snap a rotation, or fire faster than the weapon allows.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    [RequireComponent(typeof(CharacterInput))]
    public abstract class BotBrain : MonoBehaviour
    {
        CharacterInput characterInput;

        /// <summary>The channel this brain writes into. Null if a human took over this body.</summary>
        protected BotInputSource Bot => characterInput != null ? characterInput.Bot : null;

        protected virtual void Awake()
        {
            characterInput = GetComponent<CharacterInput>();
        }

        void Update()
        {
            var bot = Bot;
            if (bot == null) return;   // a player is driving; stay out of the way
            Think(bot, Time.deltaTime);
        }

        /// <summary>
        /// Decide what this character wants to do this frame and write it into
        /// <paramref name="bot"/>. Called only when this body is genuinely AI-driven.
        /// </summary>
        protected abstract void Think(BotInputSource bot, float deltaTime);
    }
}
