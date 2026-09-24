using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RigExecutor : MonoBehaviour
{
    [System.Serializable]
    public class RigListComponent
    {
        public RigList rigList;
        public bool active = true;
    }

    public bool rigActive = true;
    [SerializeField] private List<RigListComponent> rigListComponents = new List<RigListComponent>();
    [SerializeField] private List<LocalRig> localRigs = new List<LocalRig>();

    public List<IRigExtension> BeforeGlobalRigExtensions = new List<IRigExtension>();
    public List<IRigExtension> AfterGlobalRigExtensions = new List<IRigExtension>();


    private void Awake()
    {
        RigsInitialized();
    }

    public void RigsInitialized()
    {
        foreach (var rigListComponent in rigListComponents)
        {
            if (!rigListComponent.active | rigListComponent.rigList == null) continue;

            localRigs.AddRange(rigListComponent.rigList.GetLocalRigs());
        }

        foreach (var localRig in localRigs)
        {
            localRig.Initialize();

            BeforeGlobalRigExtensions.AddRange(localRig.GetBeforeAllRigExecuteExtensions());
            AfterGlobalRigExtensions.AddRange(localRig.GetAfterAllRigExecuteExtensions());
        }
    }

    void LateUpdate()
    {
        if (!rigActive) return;

        /*  foreach (var localRig in localRigs)
         {
             foreach (var extension in BeforeGlobalRigExtensions)
             {
                 extension.Execute();
             }
         }
  */ 
        foreach (var extension in BeforeGlobalRigExtensions)
        {
            extension.Execute();
        }

        foreach (var localRig in localRigs)
        {
            localRig.RigUpdate();
        }

        /*   foreach (var localRig in localRigs)
          {
              foreach (var extension in AfterGlobalRigExtensions)
              {
                  extension.Execute();
              }
          } */
        foreach (var extension in AfterGlobalRigExtensions)
        {
            extension.Execute();
        }
    }
}
