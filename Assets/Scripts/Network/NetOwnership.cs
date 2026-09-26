using UnityEngine;

/// <summary>
/// Helper for cosmetic systems that must only react for the LOCAL player
/// (camera shake, screen effects). Null-safe for offline rigs.
/// </summary>
public static class NetOwnership
{
    public static bool IsLocal(Component c)
    {
        if (c == null) return false;
        var netObject = c.GetComponentInParent<FishNet.Object.NetworkObject>();
        return netObject == null || netObject.IsOwner;
    }
}
