using UnityEngine;

/// <summary>
/// Sprint and tactical-sprint numbers, in one asset.
///
/// These used to live as serialized fields on a weapon-pose component, which meant the values
/// that decide how fast the character runs were attached to the thing that tilts the gun, and
/// every character prefab carried its own drifting copy. Pulling them into an asset means one
/// place to tune, shared by every character, and a designer can adjust feel without touching a
/// prefab or a script.
///
/// A null reference is fine everywhere: <see cref="CharacterMove"/> falls back to a built-in
/// default with these same values, so a character with no asset assigned still behaves.
/// </summary>
[CreateAssetMenu(fileName = "MovementTuning", menuName = "Code of Duty/Movement Tuning")]
public class MovementTuning : ScriptableObject
{
    [Header("Sprint")]
    [Tooltip("How far forward the stick or key must be held before sprint engages. "
           + "ONE threshold, shared by the sprint decision and the tac-sprint decision.")]
    [Range(0f, 1f)]
    public float forwardInputThreshold = 0.35f;

    [Header("Tactical sprint")]
    [Tooltip("Master switch for tactical sprint.")]
    public bool tacSprintEnabled = true;

    [Tooltip("Maximum gap between the two Sprint taps that trigger a tac sprint.")]
    public float doubleTapWindow = 0.35f;

    [Tooltip("In AUTO mode, how long sprint must be held continuously before tac sprint engages.")]
    public float autoEngageHoldTime = 1.1f;

    [Tooltip("How long a tac sprint lasts before dropping back to a normal sprint.")]
    public float duration = 3.5f;

    [Tooltip("Speed multiplier applied on top of sprint speed while tac sprinting.")]
    public float speedMultiplier = 1.22f;

    static MovementTuning fallback;

    /// <summary>
    /// Shared defaults for characters with no asset assigned. Created once, never written to,
    /// so a missing reference degrades to sensible behaviour instead of a null check at every
    /// call site.
    /// </summary>
    public static MovementTuning Default
    {
        get
        {
            if (fallback == null)
            {
                fallback = CreateInstance<MovementTuning>();
                fallback.name = "MovementTuning (built-in default)";
                fallback.hideFlags = HideFlags.HideAndDontSave;
            }
            return fallback;
        }
    }
}
