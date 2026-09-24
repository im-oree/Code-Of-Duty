using System.Collections.Generic;
using UnityEngine;

public class RigList : MonoBehaviour
{
    [System.Serializable]
    public class RigComponent
    {
        public LocalRig localRig;
        public bool mActive = true;
    }

    public List<RigComponent> rigComponents = new List<RigComponent>();

    public List<LocalRig> GetLocalRigs()
    {
        List<LocalRig> returnedList = new List<LocalRig>();

        foreach (var rigComponent in rigComponents)
        {
            if (!rigComponent.mActive | rigComponent.localRig == null) continue;

            returnedList.Add(rigComponent.localRig);
        }
        return returnedList;
    }
}
