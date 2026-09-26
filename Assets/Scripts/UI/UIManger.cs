using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIManger : MonoBehaviour
{
    public static UIManger instance;

    public KillPanel killPanel;

    public HealthPanel healthPanel;

    public RespawnPanel respawnPanel;

    void Start()
    {
        if (instance != null) Destroy(instance.gameObject);

        instance = this;
    }
}
