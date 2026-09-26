using System;
using System.Collections.Generic;

namespace CodeOfDuty.Match
{
    public enum TeamId { None, A, B, FFA }

    /// <summary>
    /// The rules of a match, as data. A mode is a DESCRIPTION, never behaviour — `MatchSystem`
    /// reads these values, which is exactly what makes custom matches possible: the host edits
    /// the numbers and the same code runs. A mode that needed its own branch in the match loop
    /// could not be tuned this way.
    ///
    /// Numbers are the real Call of Duty ones (see Docs/research/game-modes.md).
    /// </summary>
    [Serializable]
    public class GameModeDefinition
    {
        public string id;
        public string displayName;
        public string description;

        /// <summary>Team modes pool kills into a team score; solo modes score per player.</summary>
        public bool teamBased;

        /// <summary>Kills (or points) that end the match early.</summary>
        public int scoreLimit;

        /// <summary>Seconds on the match clock.</summary>
        public int timeLimitSeconds;

        /// <summary>Total players the mode is balanced for.</summary>
        public int maxPlayers;

        /// <summary>Seconds between dying and being allowed back in.</summary>
        public float respawnDelaySeconds;

        /// <summary>Pre-match countdown, as COD does before the first spawn.</summary>
        public float startCountdownSeconds;

        /// <summary>How many placings count as a win (FFA's "top three").</summary>
        public int winningPlaces;

        /// <summary>Points awarded per kill. FFA/TDM count kills directly so the HUD reads "12/30".</summary>
        public int pointsPerKill = 1;

        public GameModeDefinition Clone() => (GameModeDefinition)MemberwiseClone();

        public override string ToString() =>
            $"{displayName} ({id}) {scoreLimit} / {timeLimitSeconds}s / {maxPlayers}p";
    }

    /// <summary>A custom match is the same definition with fields overridden.</summary>
    [Serializable]
    public class GameModeOverrides
    {
        public int? scoreLimit;
        public int? timeLimitSeconds;
        public int? maxPlayers;
        public int? respawnDelaySeconds;
        public int? startCountdownSeconds;
        public int? winningPlaces;

        public bool IsEmpty =>
            !scoreLimit.HasValue && !timeLimitSeconds.HasValue && !maxPlayers.HasValue &&
            !respawnDelaySeconds.HasValue && !startCountdownSeconds.HasValue && !winningPlaces.HasValue;
    }

    public static class GameModes
    {
        // ---- Free-For-All: 8 players, 30 kills, 10 minutes, top 3 win (COD standard) ----
        public static readonly GameModeDefinition FreeForAll = new GameModeDefinition
        {
            id = "ffa",
            displayName = "Free-For-All",
            description = "Everyone for themselves. First to the score limit ends it.",
            teamBased = false,
            scoreLimit = 30,
            timeLimitSeconds = 600,
            maxPlayers = 8,
            respawnDelaySeconds = 3f,
            startCountdownSeconds = 5f,
            winningPlaces = 3,
            pointsPerKill = 1,
        };

        // ---- Team Deathmatch: 6v6, 75 kills, 10 minutes ----
        public static readonly GameModeDefinition TeamDeathmatch = new GameModeDefinition
        {
            id = "tdm",
            displayName = "Team Deathmatch",
            description = "Two teams. First to the score limit wins.",
            teamBased = true,
            scoreLimit = 75,
            timeLimitSeconds = 600,
            maxPlayers = 12,
            respawnDelaySeconds = 5f,
            startCountdownSeconds = 5f,
            winningPlaces = 1,
            pointsPerKill = 1,
        };

        public static readonly GameModeDefinition[] All = { FreeForAll, TeamDeathmatch };

        public static GameModeDefinition Get(string id)
        {
            foreach (var mode in All)
                if (mode.id == id) return mode;
            return FreeForAll;
        }

        /// <summary>Modes that random playlists may pick (excludes prototype/private-only modes).</summary>
        public static readonly GameModeDefinition[] Rotation = { FreeForAll, TeamDeathmatch };

        public static GameModeDefinition Customise(GameModeDefinition baseMode, GameModeOverrides overrides)
        {
            var result = baseMode.Clone();
            if (overrides == null) return result;
            if (overrides.scoreLimit.HasValue) result.scoreLimit = overrides.scoreLimit.Value;
            if (overrides.timeLimitSeconds.HasValue) result.timeLimitSeconds = overrides.timeLimitSeconds.Value;
            if (overrides.maxPlayers.HasValue) result.maxPlayers = overrides.maxPlayers.Value;
            if (overrides.respawnDelaySeconds.HasValue) result.respawnDelaySeconds = overrides.respawnDelaySeconds.Value;
            if (overrides.startCountdownSeconds.HasValue) result.startCountdownSeconds = overrides.startCountdownSeconds.Value;
            if (overrides.winningPlaces.HasValue) result.winningPlaces = overrides.winningPlaces.Value;
            return result;
        }
    }

    /// <summary>Hard bounds on what a custom match may ask for.</summary>
    public static class RuleLimits
    {
        public const int ScoreLimitMin = 1, ScoreLimitMax = 500;
        public const int TimeLimitMin = 60, TimeLimitMax = 3600;
        public const int MaxPlayersMin = 2, MaxPlayersMax = 32;
        public const int RespawnDelayMin = 0, RespawnDelayMax = 30;

        /// <summary>
        /// A custom match is hosted by a CLIENT, so its numbers arrive over the wire and are
        /// untrusted input. Clamp everything: a score limit of NaN would make the match
        /// unendable, and a huge maxPlayers would spawn bots until the process died.
        /// </summary>
        public static GameModeOverrides Sanitise(int? scoreLimit, int? timeLimitSeconds,
            int? maxPlayers, int? respawnDelaySeconds)
        {
            return new GameModeOverrides
            {
                scoreLimit = Clamp(scoreLimit, ScoreLimitMin, ScoreLimitMax),
                timeLimitSeconds = Clamp(timeLimitSeconds, TimeLimitMin, TimeLimitMax),
                maxPlayers = Clamp(maxPlayers, MaxPlayersMin, MaxPlayersMax),
                respawnDelaySeconds = Clamp(respawnDelaySeconds, RespawnDelayMin, RespawnDelayMax),
            };
        }

        static int? Clamp(int? value, int min, int max)
        {
            if (!value.HasValue) return null;
            return Math.Min(max, Math.Max(min, value.Value));
        }
    }
}
