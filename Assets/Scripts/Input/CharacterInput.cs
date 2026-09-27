using UnityEngine;

namespace CodeOfDuty.Input
{
    /// <summary>
    /// The one place a character's intent comes from, and the seam that makes a bot
    /// indistinguishable from a player.
    ///
    /// Every character — human or AI — carries this component. Gameplay code asks it what the
    /// character wants to do and never asks a device, so there is no "if (isBot)" branch
    /// anywhere in movement, weapons or character state. Swapping who is driving is swapping
    /// one object:
    ///
    ///   - a <see cref="PlayerInputSource"/> component present on the character  -> a human is driving
    ///   - nothing present                                                       -> a <see cref="BotInputSource"/>
    ///     is created and an AI behaviour writes into it
    ///
    /// A future, stronger AI only has to write better values into the same setters. It inherits
    /// every movement rule, weapon timing and animation transition for free, because it is
    /// travelling the identical code path a player's keystrokes travel.
    ///
    /// Execution order is pinned early so the frame's intent is polled once, before anything
    /// reads it. Unity does not otherwise guarantee that a source's own Update runs before its
    /// consumers', which would show up as a one-frame input lag that is miserable to diagnose.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public class CharacterInput : MonoBehaviour, ILocalOnly
    {
        IInputSource source;
        BotInputSource bot;

        /// <summary>Where this character's intent comes from this frame. Never null.</summary>
        public IInputSource Source
        {
            get
            {
                if (source == null) Resolve();
                return source;
            }
        }

        /// <summary>
        /// The bot channel, creating it if this character is not player-driven.
        /// Returns null for a human-driven character, which is the correct answer:
        /// AI must not fight a player for control of the same body.
        /// </summary>
        public BotInputSource Bot
        {
            get
            {
                if (source == null) Resolve();
                return bot;
            }
        }

        /// <summary>True when a human is driving this character.</summary>
        public bool IsPlayerDriven => Source is PlayerInputSource;

        void Awake() => Resolve();

        /// <summary>
        /// Find the one input owner for the character <paramref name="owner"/> belongs to,
        /// creating it on the character root if it does not exist yet.
        ///
        /// Everything resolves through here on purpose. Components sit at different depths of a
        /// character hierarchy, so if each one searched in its own direction — one upward, one
        /// downward — two components on the same body could end up bound to two different
        /// sources. Movement would then obey a bot while the weapons obeyed a player, which
        /// presents as "the bot shoots but won't walk" and is thoroughly unpleasant to track
        /// down. One resolver, one answer.
        /// </summary>
        public static CharacterInput For(Component owner)
        {
            if (owner == null) return null;

            var existing = owner.GetComponentInParent<CharacterInput>();
            if (existing != null) return existing;

            existing = owner.transform.root.GetComponentInChildren<CharacterInput>();
            if (existing != null) return existing;

            // Attach to the character root, so every component below finds the same instance.
            var move = owner.GetComponentInParent<CharacterMove>();
            GameObject host = move != null ? move.gameObject : owner.transform.root.gameObject;
            return host.AddComponent<CharacterInput>();
        }

        void Resolve()
        {
            var player = GetComponent<PlayerInputSource>();
            if (player != null)
            {
                source = player;
                bot = null;
                return;
            }

            bot ??= new BotInputSource();
            source = bot;
        }

        /// <summary>
        /// Convert a character into a bot-driven one, and return the channel to write into.
        ///
        /// The player prefab ships with a <see cref="PlayerInputSource"/> attached, because the
        /// common case is a human and a character that silently defaulted to an unwritten bot
        /// source would simply stand still. A bot spawner therefore calls this on the body it
        /// just created: the human input component is removed outright rather than disabled, so
        /// there is no chance of a stray device read reaching a bot.
        /// </summary>
        public BotInputSource MakeBotDriven()
        {
            var player = GetComponent<PlayerInputSource>();
            if (player != null)
            {
                if (Application.isPlaying) Destroy(player);
                else DestroyImmediate(player);
            }

            bot = new BotInputSource();
            source = bot;
            return bot;
        }

        /// <summary>
        /// Hand control to an explicit source. Used by spawners that decide at runtime whether a
        /// body is filled by a player or a bot, and by tests that want to script exact input.
        /// </summary>
        public void SetSource(IInputSource newSource)
        {
            source = newSource;
            bot = newSource as BotInputSource;
        }

        void Update()
        {
            // Exactly once per frame, before any consumer reads. Sources that sample devices poll
            // here; sources that are written to externally clear their edge state here.
            Source.Tick(Time.deltaTime);
        }

        /* ---- Convenience pass-throughs, so callers read like intent rather than plumbing ---- */

        public Vector2 Move => Source.Move;
        public Vector2 Look => Source.Look;
        public float Lean => Source.Lean;

        public bool Held(InputActionId action) => Source.Held(action);
        public bool Pressed(InputActionId action) => Source.Pressed(action);
        public bool Released(InputActionId action) => Source.Released(action);
    }
}
