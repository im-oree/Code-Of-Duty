using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public abstract class LocalRig : MonoBehaviour
{
    public List<IRigExtension> AllLocalRigExtensions { get; private set; } = new List<IRigExtension>();
    public List<IRigExtension> BeforeLocalRigExecuteExtensions { get; private set; } = new List<IRigExtension>();
    public List<IRigExtension> AfterLocalRigExecuteExtensions { get; private set; } = new List<IRigExtension>();
    public List<IRigExtension> AfterOnRenderExtensions { get; private set; } = new List<IRigExtension>();

    public void Initialize()
    {
        AllLocalRigExtensions.AddRange(GetComponents<IRigExtension>());

        foreach (var extensions in AllLocalRigExtensions)
        {
            extensions.Initialize(this);

            if (extensions.updateMethod == RigExtensionUpdateMethod.BeforeLocalRigExecute)
            {
                BeforeLocalRigExecuteExtensions.Add(extensions);
                continue;
            }
            if (extensions.updateMethod == RigExtensionUpdateMethod.AfterLocalRigExecute)
            {
                AfterLocalRigExecuteExtensions.Add(extensions);
                continue;
            }
            if (extensions.updateMethod == RigExtensionUpdateMethod.AfterOnRender)
            {
                AfterOnRenderExtensions.Add(extensions);
                continue;
            }
        }
    }

    private void BeforeLocalRigUpdate()
    {
        foreach (var rigExtension in BeforeLocalRigExecuteExtensions)
        {
            rigExtension.Execute();
        }
    }

    private void AfterLocalRigUpdate()
    {

        foreach (var rigExtension in AfterLocalRigExecuteExtensions)
        {
            rigExtension.Execute();
            Debug.Log("bn" + rigExtension.updateMethod);
        }
    }

    public abstract void Execute();

    public void RigUpdate()
    {

        BeforeLocalRigUpdate();

        Execute();

        AfterLocalRigUpdate();
    }

    private void OnPostRender()
    {
        foreach (var rigExtension in AfterOnRenderExtensions)
        {
            rigExtension.Execute();
        }
    }

    public List<IRigExtension> GetBeforeAllRigExecuteExtensions()
    {

        List<IRigExtension> beforeAllrigExecuteExtensions = new List<IRigExtension>();

        foreach (var extensions in AllLocalRigExtensions)
        {

            if (extensions.updateMethod == RigExtensionUpdateMethod.BeforeAllRigExecute)
            {
                beforeAllrigExecuteExtensions.Add(extensions);
                Debug.Log("2 " + transform.name);
                continue;
            }
        }

        return beforeAllrigExecuteExtensions;
    }

    public List<IRigExtension> GetAfterAllRigExecuteExtensions()
    {
        List<IRigExtension> afterAllrigExecuteExtensions = new List<IRigExtension>();

        foreach (var extensions in AllLocalRigExtensions)
        {
            if (extensions.updateMethod == RigExtensionUpdateMethod.AfterAllRigExecute)
            {
                afterAllrigExecuteExtensions.Add(extensions);
                continue;
            }
        }

        return afterAllrigExecuteExtensions;
    }
}
