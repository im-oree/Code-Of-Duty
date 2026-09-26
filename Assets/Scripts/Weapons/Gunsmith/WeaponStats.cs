using UnityEngine;

namespace CodeOfDuty.Gunsmith
{
    /// <summary>
    /// The six axes Call of Duty shows on the weapon bar chart: accuracy, damage, range,
    /// fire rate, mobility, control. Base values live on the weapon; attachments contribute
    /// deltas that are summed and clamped.
    /// </summary>
    [System.Serializable]
    public struct WeaponStats
    {
        [Range(0f, 100f)] public float accuracy;
        [Range(0f, 100f)] public float damage;
        [Range(0f, 100f)] public float range;
        [Range(0f, 100f)] public float fireRate;
        [Range(0f, 100f)] public float mobility;
        [Range(0f, 100f)] public float control;

        public WeaponStats(float accuracy, float damage, float range, float fireRate, float mobility, float control)
        {
            this.accuracy = accuracy;
            this.damage = damage;
            this.range = range;
            this.fireRate = fireRate;
            this.mobility = mobility;
            this.control = control;
        }

        public static WeaponStats operator +(WeaponStats a, WeaponStats b) => new WeaponStats(
            a.accuracy + b.accuracy, a.damage + b.damage, a.range + b.range,
            a.fireRate + b.fireRate, a.mobility + b.mobility, a.control + b.control);

        /// <summary>Clamp to the displayable 0..100 band after all attachments are applied.</summary>
        public WeaponStats Clamped() => new WeaponStats(
            Mathf.Clamp(accuracy, 0f, 100f), Mathf.Clamp(damage, 0f, 100f), Mathf.Clamp(range, 0f, 100f),
            Mathf.Clamp(fireRate, 0f, 100f), Mathf.Clamp(mobility, 0f, 100f), Mathf.Clamp(control, 0f, 100f));

        public float Get(int axis)
        {
            switch (axis)
            {
                case 0: return accuracy;
                case 1: return damage;
                case 2: return range;
                case 3: return fireRate;
                case 4: return mobility;
                case 5: return control;
                default: return 0f;
            }
        }

        public static readonly string[] AxisNames =
            { "Accuracy", "Damage", "Range", "Fire Rate", "Mobility", "Control" };
    }

    /// <summary>The eight attachment areas on a weapon, plus the weapon-perk slot.</summary>
    public enum AttachmentSlot
    {
        Muzzle,
        Laser,
        Optic,
        Stock,
        RearGrip,
        Magazine,
        Underbarrel,
        Barrel,
        WeaponPerk,
    }

    /// <summary>
    /// One attachment. Data only: the loadout aggregates its <see cref="statDelta"/> onto the
    /// weapon's base <see cref="WeaponStats"/> and shows the pro/con text on the stat bars.
    /// See Docs/research/gunsmith.md.
    /// </summary>
    [CreateAssetMenu(fileName = "Attachment", menuName = "COD/Gunsmith/Attachment")]
    public class AttachmentDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public AttachmentSlot slot;
        public string displayName;

        [TextArea] public string description;

        [Header("Gunsmith text")]
        [Tooltip("The upside, shown in the attachment tooltip.")]
        public string pro;
        [Tooltip("The downside, shown in the attachment tooltip.")]
        public string con;

        [Header("Stat effect")]
        public WeaponStats statDelta;

        [Header("Unlock + visuals")]
        [Tooltip("Weapon level at which this attachment unlocks.")]
        public int unlockedAtWeaponLevel;

        [Tooltip("Child transform on the weapon prefab that receives the override model.")]
        public string socketName;

        [Tooltip("Optional mesh/model swapped in when equipped (optic, barrel, magazine…).")]
        public GameObject meshOverride;
    }

    /// <summary>How many attachments a weapon may carry, and the wildcard that raises it.</summary>
    public static class GunsmithRules
    {
        public const int DefaultAttachmentSlots = 5;
        /// <summary>With the Gunfighter-style wildcard.</summary>
        public const int MaxAttachmentSlots = 8;
    }
}
