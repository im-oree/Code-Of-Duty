// Compile-check-only polyfills (NOT part of the game, never imported by Unity —
// this folder has no .meta files and is only fed to the sandbox csc).
// The Unity player reference set is the mscorlib 4.x profile, which lacks the
// marker type C# 9 records/init-setters need.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
