using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// Disables local-only components (camera, input, movement, etc.)
/// on player objects that don't belong to this client.
///
/// Two sources, on purpose:
///
/// * <b><see cref="ILocalOnly"/></b> — anything we write marks itself, and is found here
///   automatically. This is the one that should be used. The serialized list below cannot be
///   kept correct by hand: it had already gone stale once, because components added to the
///   player after it was authored were never added to it, and one of them read the local
///   keyboard on every remote body in the match.
/// * <b>The serialized lists</b> — for things that cannot implement an interface: native
///   components like <see cref="Camera"/>, third-party scripts, and whole GameObjects.
/// </summary>
public class NetComponentEnabler : NetworkBehaviour
{
    [SerializeField] private List<MonoBehaviour> disableComponents;
    [SerializeField] private List<GameObject> inactiveGameObjects;
    [SerializeField] private Camera playerCamera;

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner)
        {
            ComponentsDisaber();
        }
    }

    /// <summary>
    /// True when an AI is driving this body rather than a person. Bots run their character
    /// through the same components a player does, so a bot's body must keep them.
    /// </summary>
    /// <remarks>
    /// This reads the input source, so a bot spawner has to hand the body its
    /// <c>BotInputSource</c> BEFORE calling <c>ServerManager.Spawn</c> — otherwise the source
    /// is still the default player one at this point and the bot's own components get switched
    /// off on the server. Nothing depends on that yet; bots do not exist. It is written down
    /// because it will be load-bearing the moment they do.
    /// </remarks>
    bool IsBotDriven
    {
        get
        {
            var characterInput = GetComponentInChildren<CodeOfDuty.Input.CharacterInput>(true);
            return characterInput != null && !characterInput.IsPlayerDriven;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // dedicated server: nothing is local, disable everything local-only
        if (!InstanceFinder.IsClientStarted)
        {
            ComponentsDisaber();
        }
    }

    void ComponentsDisaber()
    {
        if (IsBotDriven) return;

        // Null guards throughout: these are Inspector lists, and an entry whose component was
        // deleted from the prefab serializes as null. Throwing here would abort the rest of the
        // list and leave a half-disabled remote body, which is worse than a missing entry.
        if (playerCamera != null) playerCamera.enabled = false;

        foreach (var item in disableComponents)
        {
            if (item != null) item.enabled = false;
        }

        // Everything that declared itself local-only.
        foreach (var local in GetComponentsInChildren<ILocalOnly>(true))
        {
            if (local is MonoBehaviour behaviour && behaviour != null) behaviour.enabled = false;
        }

        foreach (var item in inactiveGameObjects)
        {
            if (item != null) item.SetActive(false);
        }
    }
}
