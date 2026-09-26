using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Captures a play-mode screenshot so poses (tac sprint, aim, VIEWMODEL framing) can be
/// reviewed from first AND third person without recording video.
///
/// While in play mode: press <b>Cmd/Ctrl + Shift + S</b> or use <b>COD / Debug / Capture
/// Game Screenshot</b>. Files land in <c>&lt;project&gt;/Screenshots/</c>.
///
/// Suggested review pass for tac sprint:
///   1. Play DMArena1, run forward, double-tap sprint.
///   2. Capture at the tac-sprint peak (first person).
///   3. Press V to toggle third person, capture again.
/// The two frames should show the same story: muzzle up, gun pulled in, off-hand tucked —
/// never a limp or detached arm, and never the weapon outside the frame.
/// </summary>
public static class GameScreenshotTool
{
    [MenuItem("COD/Debug/Capture Game Screenshot %#s")]
    public static void Capture()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[COD] Screenshot capture only works in play mode — press Play first.");
            return;
        }

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"cod_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

        ScreenCapture.CaptureScreenshot(path);
        Debug.Log($"[COD] screenshot -> {path}");
    }
}
