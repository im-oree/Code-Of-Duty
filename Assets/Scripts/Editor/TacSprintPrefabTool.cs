using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Re-serializes the tac-sprint pose fields on the player prefab.
///
/// WHY THIS EXISTS
/// Player.prefab was saved by an older revision of <see cref="WeaponMovementPose"/>,
/// so its YAML stores only <c>weaponController</c> and <c>characterMove</c>. Every
/// pose field — the offsets, blend speed, the off-hand tuck point — is missing from
/// the asset. Unity fills those gaps from the field initializers at load time, so
/// the game still runs on code defaults, but the Inspector shows nothing to look at
/// or tune. That is the "everything must be editable in the scene/prefab" rule
/// being quietly broken, and it makes pose work impossible to iterate on visually.
///
/// Loading the prefab contents, touching the component and saving it back makes
/// Unity write out every field, and repairs the null wiring while it is there.
/// </summary>
public static class TacSprintPrefabTool
{
    public const string PrefabPath = "Assets/Prefabs/Player.prefab";

    [MenuItem("COD/Weapons/Re-serialize Player Pose Fields")]
    public static void ReserializePoseFields()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
        {
            Debug.LogError($"[TacSprint] could not load {PrefabPath}");
            return;
        }

        try
        {
            var pose = root.GetComponentInChildren<WeaponMovementPose>(true);
            if (pose == null)
            {
                Debug.LogError($"[TacSprint] no WeaponMovementPose found in {PrefabPath}");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"[TacSprint] re-serializing {PrefabPath}");

            // Repair the null wiring so the Inspector shows genuine references.
            if (pose.weaponController == null)
            {
                pose.weaponController = root.GetComponentInChildren<WeaponController>(true);
                report.AppendLine("  repaired weaponController = " + Describe(pose.weaponController));
            }
            if (pose.characterMove == null)
            {
                pose.characterMove = root.GetComponentInChildren<CharacterMove>(true);
                report.AppendLine("  repaired characterMove    = " + Describe(pose.characterMove));
            }
            if (pose.thirdPersonWeapon == null)
                report.AppendLine("  thirdPersonWeapon         = unassigned (no third-person weapon transform exists yet)");

            EditorUtility.SetDirty(pose);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();

            report.AppendLine("  tacSprintEnabled         = " + pose.tacSprintEnabled);
            report.AppendLine($"  tacPositionOffset        = {pose.tacPositionOffset}");
            report.AppendLine($"  tacEulerOffset           = {pose.tacEulerOffset}");
            report.AppendLine($"  tacOneHandedEulerOffset  = {pose.tacOneHandedEulerOffset}");
            report.AppendLine("  maxMuzzleUpDegrees       = " + pose.maxMuzzleUpDegrees);
            report.AppendLine($"  tacOffHandTuckLocal      = {pose.tacOffHandTuckLocal}");
            report.AppendLine("  retargetOffHand          = " + pose.retargetOffHand);
            report.AppendLine("  thirdPersonPoseScale     = " + pose.thirdPersonPoseScale);

            Debug.Log(report.ToString());
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static string Describe(UnityEngine.Object o) => o == null ? "null" : o.name;
}
