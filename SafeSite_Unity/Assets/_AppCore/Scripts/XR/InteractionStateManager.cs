using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

/// <summary>
/// Decides what the user may do right now: grab things, teleport, use UI rays. Call
/// <see cref="SetMode"/> from a scenario (find it with FindFirstObjectByType); the modes are
/// described in Assets/_AppCore/Player/README.md. The interactor lists are wired in --Player--.prefab.
/// </summary>
public class InteractionStateManager : MonoBehaviour
{
    [Header("Direct Interactors")]
    [SerializeField]
    private GameObject[] grabInteractors;

    [Header("Teleport / UI Interactors")]
    [SerializeField]
    private GameObject[] farInteractors;

    [Header("Controller Rays")]
    [Tooltip("The controllers' Near-Far Interactors: they grab and are also the controllers' UI ray. " +
             "In UI And Teleport they stay on for UI but can't grab anything.")]
    [SerializeField]
    private NearFarInteractor[] controllerRays;

    public enum InteractionMode
    {
        None,
        ToolUse,
        UIAndTeleport,
        Full
    }

    [Header("Current Mode")]
    [SerializeField]
    private InteractionMode currentMode;

    public InteractionMode CurrentMode => currentMode;

    // Each controller ray's own interaction layers, put back when it may grab again.
    private readonly Dictionary<NearFarInteractor, InteractionLayerMask> rayLayers = new Dictionary<NearFarInteractor, InteractionLayerMask>();

    private void Awake()
    {
        foreach (var ray in controllerRays)
        {
            if (ray != null)
                rayLayers[ray] = ray.interactionLayers;
        }

        SetMode(currentMode);
    }

    public void SetGrab(bool enabled)
    {
        SetGroup(grabInteractors, enabled);
    }

    public void SetFar(bool enabled)
    {
        foreach (GameObject interactor in farInteractors)
        {
            if (interactor == null)
                continue;

            // A controller's Teleport Interactor belongs to its ControllerInputActionManager, which shows it
            // only while the thumbstick is pushed forward. Forcing it active here left the teleport ray on
            // all the time, so allow or block teleporting through that manager instead.
            var owner = interactor.GetComponentInParent<ControllerInputActionManager>(true);
            if (owner != null)
            {
                owner.enabled = enabled;
                if (!enabled)
                    interactor.SetActive(false);
                continue;
            }

            interactor.SetActive(enabled);
        }
    }


    public void DisableAll()
    {
        SetGrab(false);
        SetFar(false);

    }

    public void EnableAll()
    {
        SetGrab(true);
        SetFar(true);
    }

    private void SetGroup(GameObject[] interactors, bool enabled)
    {
        foreach (GameObject interactor in interactors)
        {
            if (interactor != null)
                interactor.SetActive(enabled);
        }
    }

    /// <summary>Controller rays on or off; while on, <paramref name="canGrab"/> decides whether they can grab.</summary>
    private void SetControllerRays(bool active, bool canGrab)
    {
        foreach (var ray in controllerRays)
        {
            if (ray == null)
                continue;
            ray.gameObject.SetActive(active);
            // Layers only change while playing, so the scene keeps the rays' real layers.
            if (Application.isPlaying && rayLayers.TryGetValue(ray, out var layers))
                ray.interactionLayers = canGrab ? layers : 0;
        }
    }

    public void SetMode(InteractionMode mode)
    {
        currentMode = mode;
        switch (mode)
        {
            case InteractionMode.None:
                DisableAll();
                SetControllerRays(false, false);
                break;

            case InteractionMode.ToolUse:
                SetGrab(true);
                SetFar(false);
                SetControllerRays(true, true);
                break;

            case InteractionMode.UIAndTeleport:
                SetGrab(false);
                SetFar(true);
                SetControllerRays(true, false);
                break;

            case InteractionMode.Full:
                EnableAll();
                SetControllerRays(true, true);
                break;
        }
    }
#if UNITY_EDITOR
    private void OnValidate()
    {
        SetMode(currentMode);
    }
#endif
}
