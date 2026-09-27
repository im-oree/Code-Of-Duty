using Invector.vCharacterController;
using Invector.vEventSystems;
using UnityEngine;

/// <summary>
/// Code Of Duty player controller — the Invector vThirdPersonController with
/// network-aware damage routing. When a <see cref="CODNetworkHealth"/> lives on
/// the same object and the object is network-spawned, damage is routed through
/// the server so every client stays in sync; offline it behaves exactly like
/// the stock Invector controller.
/// </summary>
public class CODThirdPersonController : vThirdPersonController
{
    CODNetworkHealth _netHealth;
    bool _netHealthResolved;

    CODNetworkHealth NetHealth
    {
        get
        {
            if (!_netHealthResolved)
            {
                _netHealth = GetComponent<CODNetworkHealth>();
                _netHealthResolved = true;
            }
            return _netHealth;
        }
    }

    public override void TakeDamage(Invector.vDamage damage)
    {
        // Networked: hand the damage to CODNetworkHealth, which raises it on
        // the server and replicates it to every client (including this one).
        if (NetHealth != null && NetHealth.InterceptDamage(damage))
        {
            return;
        }

        base.TakeDamage(damage);
    }

    /// <summary>Applies damage locally, bypassing network interception. Called by CODNetworkHealth on each client.</summary>
    public void ApplyDamageDirect(Invector.vDamage damage)
    {
        base.TakeDamage(damage);
    }
}
