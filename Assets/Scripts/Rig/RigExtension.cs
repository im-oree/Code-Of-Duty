using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public abstract class RigExtension<T> : MonoBehaviour, IRigExtension where T : LocalRig
{
    [HideInInspector] public T baseLocalRig { get; private set; }
    public RigExtensionUpdateMethod _rigExtensionUpdateMethod;
    public RigExtensionUpdateMethod updateMethod { get => _rigExtensionUpdateMethod; set => _rigExtensionUpdateMethod = value; }

    public void Initialize(LocalRig localRig)
    {
        baseLocalRig = (T)localRig;

        if (baseLocalRig == null)
        {
            Debug.LogError("Error Extension localRig Type");
        }
    }

    public abstract void Tick();
    public void Execute()
    {
        if (baseLocalRig == null) return;

        Tick();
    }
}
