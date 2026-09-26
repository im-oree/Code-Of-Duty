using UnityEditor;
using UnityEngine;
using CodeOfDuty.Character;

/// <summary>
/// Parity with the reference project's <c>tools/verify/state-authority.mjs</c>: a state that
/// exists in an enum but has no transition rules is unreachable, and adding a feature that skips
/// the authority must fail loudly. Run from <b>COD / Verify Character State</b> or automatically
/// on load.
/// </summary>
public static class CharacterStateVerifier
{
    [MenuItem("COD/Verify Character State")]
    public static void Verify()
    {
        int failures = 0;

        failures += Check("Locomotion", CharacterState.TableIsComplete(CharacterState.RawLocomotionTable));
        failures += Check("Traversal", CharacterState.TableIsComplete(CharacterState.RawTraversalTable));
        failures += Check("WeaponAction", CharacterState.TableIsComplete(CharacterState.RawWeaponActionTable));
        failures += Check("Aim", CharacterState.TableIsComplete(CharacterState.RawAimTable));
        failures += Check("Carry", CharacterState.TableIsComplete(CharacterState.RawCarryTable));

        if (failures == 0)
            Debug.Log("[CharacterState] OK — every channel's transition table is complete.");
        else
            Debug.LogError($"[CharacterState] FAILED — {failures} channel(s) incomplete. " +
                           "Add the missing states to the TRANSITIONS table in CharacterState.cs.");
    }

    static int Check(string channel, bool complete)
    {
        if (complete) return 0;
        Debug.LogError($"[CharacterState] channel '{channel}' is missing enum members in its transition table.");
        return 1;
    }
}
