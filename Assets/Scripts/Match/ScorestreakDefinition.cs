using System.Collections.Generic;

namespace CodeOfDuty.Match
{
    /// <summary>How the streak is delivered/used, which decides what server system drives it.</summary>
    public enum ScorestreakCategory
    {
        Recon,      // UAV family — reveals enemies
        Support,    // deployed friendly assets (turret, sentry, helo)
        Offensive,  // airstrikes / bombardment
        Vehicle,    // player-driven ground vehicle
        Air,        // player-driven or AI aircraft
        Special,    // care packages, juggernaut, nuke
    }

    /// <summary>Every MW2019 streak. See Docs/research/scorestreaks.md for sources and behaviour.</summary>
    public enum ScorestreakKind
    {
        PersonalRadar,
        ShieldTurret,
        CounterUAV,
        UAV,
        CarePackage,
        ClusterStrike,
        CruiseMissile,
        PrecisionAirstrike,
        Wheelson,
        InfantryAssaultVehicle,
        SentryGun,
        EmergencyAirdrop,
        VTOLJet,
        ChopperGunner,
        WhitePhosphorus,
        SupportHelo,
        Gunship,
        AdvancedUAV,
        Juggernaut,
        TacticalNuke,
    }

    [System.Serializable]
    public class ScorestreakDefinition
    {
        public ScorestreakKind kind;
        public string displayName;
        /// <summary>Kills without dying required to earn it (Hardline subtracts 1).</summary>
        public int killCost;
        public ScorestreakCategory category;
        public string description;

        /// <summary>True when the player takes over a camera (Chopper Gunner, Gunship, missile, Wheelson…).</summary>
        public bool piloted;

        /// <summary>True when it is a hidden fourth streak that cannot be equipped.</summary>
        public bool hidden;

        /// <summary>True when it can be replaced/disabled on small maps (Infantry Assault Vehicle).</summary>
        public bool largeMapsOnly;
    }

    public static class Scorestreaks
    {
        /// <summary>The complete MW2019 set with real kill costs.</summary>
        public static readonly ScorestreakDefinition[] All =
        {
            Def(ScorestreakKind.PersonalRadar, "Personal Radar", 3, ScorestreakCategory.Recon,
                "Escort drone giving only its owner nearby enemy pings. Fragile and shootable."),
            Def(ScorestreakKind.ShieldTurret, "Shield Turret", 3, ScorestreakCategory.Support,
                "Deployable shielded .50 cal emplacement controlled by hand."),
            Def(ScorestreakKind.CounterUAV, "Counter UAV", 4, ScorestreakCategory.Recon,
                "Jams enemy mini-maps and progressively disrupts their HUD."),
            Def(ScorestreakKind.UAV, "UAV", 4, ScorestreakCategory.Recon,
                "Team-wide mini-map sweeps revealing enemies outside the Ghost perk."),
            Def(ScorestreakKind.CarePackage, "Care Package", 4, ScorestreakCategory.Special,
                "Marker drops a crate holding a random streak, weighted toward cheaper ones."),
            Def(ScorestreakKind.ClusterStrike, "Cluster Strike", 5, ScorestreakCategory.Offensive,
                "Laser-designated mortar barrage onto a circular zone."),
            Def(ScorestreakKind.CruiseMissile, "Cruise Missile", 5, ScorestreakCategory.Offensive,
                "Tablet-launched, player-steered missile with a boost.", piloted: true),
            Def(ScorestreakKind.PrecisionAirstrike, "Precision Airstrike", 5, ScorestreakCategory.Offensive,
                "Mark a line; two jets strafe it in sequence."),
            Def(ScorestreakKind.Wheelson, "Wheelson", 7, ScorestreakCategory.Vehicle,
                "Remote-controlled UGV with an airburst turret.", piloted: true),
            Def(ScorestreakKind.InfantryAssaultVehicle, "Infantry Assault Vehicle", 7, ScorestreakCategory.Vehicle,
                "Light tank with driver cannon and a turret gunner position.", piloted: true, largeMapsOnly: true),
            Def(ScorestreakKind.SentryGun, "Sentry Gun", 7, ScorestreakCategory.Support,
                "Automated turret that swivels to track enemies."),
            Def(ScorestreakKind.EmergencyAirdrop, "Emergency Airdrop", 8, ScorestreakCategory.Special,
                "Three care packages in a single drop."),
            Def(ScorestreakKind.VTOLJet, "VTOL Jet", 8, ScorestreakCategory.Air,
                "Missile barrage then hovers as a repositionable defensive platform."),
            Def(ScorestreakKind.ChopperGunner, "Chopper Gunner", 10, ScorestreakCategory.Air,
                "Player-piloted gunship: cannon, hydra rockets, thermal.", piloted: true),
            Def(ScorestreakKind.WhitePhosphorus, "White Phosphorus", 10, ScorestreakCategory.Offensive,
                "Carpet line of incendiaries; heavy disorient and burn, affects the owner too."),
            Def(ScorestreakKind.SupportHelo, "Support Helo", 11, ScorestreakCategory.Air,
                "AI-piloted circling helicopter with two auto-firing gunners."),
            Def(ScorestreakKind.Gunship, "Gunship", 12, ScorestreakCategory.Air,
                "105mm / 40mm / 25mm selectable, thermal, orbits the map.", piloted: true),
            Def(ScorestreakKind.AdvancedUAV, "Advanced UAV", 12, ScorestreakCategory.Recon,
                "Reveals enemies AND the direction they face to the whole team."),
            Def(ScorestreakKind.Juggernaut, "Juggernaut", 15, ScorestreakCategory.Special,
                "Care-package assault suit: heavy armour, minigun, limited mobility."),
            Def(ScorestreakKind.TacticalNuke, "Tactical Nuke", 30, ScorestreakCategory.Special,
                "30 kills in one life with loadout weapons only — ends the match immediately.", hidden: true),
        };

        static ScorestreakDefinition Def(ScorestreakKind kind, string name, int cost,
            ScorestreakCategory category, string description, bool piloted = false,
            bool hidden = false, bool largeMapsOnly = false)
        {
            return new ScorestreakDefinition
            {
                kind = kind,
                displayName = name,
                killCost = cost,
                category = category,
                description = description,
                piloted = piloted,
                hidden = hidden,
                largeMapsOnly = largeMapsOnly,
            };
        }

        public static ScorestreakDefinition Get(ScorestreakKind kind)
        {
            foreach (var streak in All)
                if (streak.kind == kind) return streak;
            return null;
        }

        /// <summary>Streaks a player may equip (hidden ones cannot be chosen).</summary>
        public static List<ScorestreakDefinition> Equippable()
        {
            var list = new List<ScorestreakDefinition>();
            foreach (var streak in All)
                if (!streak.hidden) list.Add(streak);
            return list;
        }

        /// <summary>Hardline subtracts one kill from the cost, never below one.</summary>
        public static int EffectiveCost(ScorestreakKind kind, bool hardline)
        {
            var def = Get(kind);
            if (def == null) return int.MaxValue;
            return hardline ? System.Math.Max(1, def.killCost - 1) : def.killCost;
        }
    }
}
