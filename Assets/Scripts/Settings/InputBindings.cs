using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central keybinding registry — THE single source of truth for controls.
/// Nothing in gameplay code references KeyCodes directly anymore.
///
/// - Actions self-register here with a default key; the settings UI simply
///   enumerates <see cref="Actions"/>, so adding a new action later means
///   adding ONE line here and the rebind UI grows automatically.
/// - Player overrides persist in PlayerPrefs ("key_&lt;id&gt;").
/// - Also owns control-style preferences (crouch hold/toggle, tac-sprint
///   trigger, fire-while-sprint) so they live next to the keys they modify.
/// </summary>
public static class InputBindings
{
    public class ActionDef
    {
        public string id;
        public string label;
        public string category;
        public KeyCode defaultKey;
    }

    static readonly List<ActionDef> actions = new List<ActionDef>();
    static readonly Dictionary<string, KeyCode> current = new Dictionary<string, KeyCode>();
    static bool initialized;

    // ----------------------------------------------------------- registry

    static void Init()
    {
        if (initialized) return;
        initialized = true;

        // MOVEMENT
        Register("sprint", "Sprint", "Movement", KeyCode.LeftShift);
        Register("jump", "Jump", "Movement", KeyCode.Space);
        Register("crouch", "Crouch / Slide", "Movement", KeyCode.C);

        // COMBAT
        Register("fire", "Fire", "Combat", KeyCode.Mouse0);
        Register("aim", "Aim Down Sights", "Combat", KeyCode.Mouse1);
        Register("sightSwitch", "Switch Sight", "Combat", KeyCode.Mouse2);
        Register("weapon1", "Primary Weapon", "Combat", KeyCode.Alpha1);
        Register("weapon2", "Secondary Weapon", "Combat", KeyCode.Alpha2);
        Register("melee", "Melee Stance", "Combat", KeyCode.Alpha3);
        Register("reload", "Reload", "Combat", KeyCode.R);
        Register("grenade", "Throw Grenade", "Combat", KeyCode.G);
        Register("tactical", "Tactical Equipment", "Combat", KeyCode.Q);
        // Unbound by default: the knife is swung with Fire while in melee stance, so a
        // dedicated key would be a second way to do the same thing. It exists because the
        // gamepad binds it to right-stick click, and because players ask for it.
        Register("meleeAttack", "Quick Melee", "Combat", KeyCode.None);
        Register("inspect", "Inspect Weapon", "Combat", KeyCode.I);

        // KILLSTREAKS
        Register("streak1", "Killstreak 1", "Killstreaks", KeyCode.Z);
        Register("streak2", "Killstreak 2", "Killstreaks", KeyCode.X);
        Register("streak3", "Killstreak 3", "Killstreaks", KeyCode.B);

        // GENERAL
        Register("interact", "Interact / Pickup", "General", KeyCode.F);
        Register("viewToggle", "FPS / TPS Camera", "General", KeyCode.V);
        Register("parachute", "Open / Close Parachute", "General", KeyCode.Space);
        Register("lean", "Lean (axis)", "General", KeyCode.None);
        Register("scoreboard", "Scoreboard", "General", KeyCode.Tab);
        Register("pause", "Pause Menu", "General", KeyCode.Escape);
    }

    public static void Register(string id, string label, string category, KeyCode defaultKey)
    {
        Init();
        foreach (var a in actions)
            if (a.id == id) return; // idempotent

        actions.Add(new ActionDef { id = id, label = label, category = category, defaultKey = defaultKey });
        current[id] = (KeyCode)PlayerPrefs.GetInt("key_" + id, (int)defaultKey);
    }

    public static IReadOnlyList<ActionDef> Actions
    {
        get { Init(); return actions; }
    }

    // ----------------------------------------------------------- get / set

    public static KeyCode Get(string id)
    {
        Init();
        return current.TryGetValue(id, out KeyCode key) ? key : KeyCode.None;
    }

    public static void Set(string id, KeyCode key)
    {
        Init();
        current[id] = key;
        PlayerPrefs.SetInt("key_" + id, (int)key);
    }

    public static void ResetToDefaults()
    {
        Init();
        foreach (var a in actions)
        {
            current[a.id] = a.defaultKey;
            PlayerPrefs.DeleteKey("key_" + a.id);
        }
    }

    // ----------------------------------------------------------- polling

    public static bool Down(string id) => Input.GetKeyDown(Get(id));
    public static bool Held(string id) => Input.GetKey(Get(id));
    public static bool Up(string id) => Input.GetKeyUp(Get(id));

    // ------------------------------------------------- control-style prefs

    /// <summary>true = crouch toggles on press; false = crouch only while held.</summary>
    public static bool CrouchIsToggle
    {
        get => PlayerPrefs.GetInt("ctl_crouchToggle", 1) == 1;
        set => PlayerPrefs.SetInt("ctl_crouchToggle", value ? 1 : 0);
    }

    /// <summary>0 = double-tap sprint (COD default), 1 = automatic after sprinting for a moment.</summary>
    public static int TacSprintMode
    {
        get => PlayerPrefs.GetInt("ctl_tacSprintMode", 0);
        set => PlayerPrefs.SetInt("ctl_tacSprintMode", value);
    }

    public static bool FireWhileSprinting
    {
        get => PlayerPrefs.GetInt("ctl_fireWhileSprint", 0) == 1;
        set => PlayerPrefs.SetInt("ctl_fireWhileSprint", value ? 1 : 0);
    }
}
