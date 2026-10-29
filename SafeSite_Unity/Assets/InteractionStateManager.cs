using UnityEditor;
using UnityEngine;

public class InteractionStateManager : MonoBehaviour
{
    [Header("Direct Interactors")]
    [SerializeField] 
    private GameObject[] grabInteractors;

    [Header("Teleport / UI Interactors")]
    [SerializeField] 
    private GameObject[] farInteractors;

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

    public void SetGrab(bool enabled)
    {
        SetGroup(grabInteractors, enabled);
    }

    public void SetFar(bool enabled)
    {
        SetGroup(farInteractors, enabled);
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

    public void SetMode(InteractionMode mode)
    {
        switch (mode)
        {
            case InteractionMode.None:
                DisableAll();
                break;

            case InteractionMode.ToolUse:
                SetGrab(true);
                SetFar(false);
                break;

            case InteractionMode.UIAndTeleport:
                SetGrab(false);
                SetFar(true);
                break;

            case InteractionMode.Full:
                EnableAll();
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