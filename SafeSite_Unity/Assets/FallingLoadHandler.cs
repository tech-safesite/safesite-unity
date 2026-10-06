using UnityEngine;

public class FallingLoadHandler : MonoBehaviour
{
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private GameObject onDropAudioParent;

    [SerializeField]
    private GameObject onImpactAudioParent;

    [SerializeField]
    private GameObject loadVisual;

    [SerializeField]
    private CameraShaker cameraShaker;

    private void Start()
    {
        loadVisual.SetActive(false);
    }
    public void DropLoad()
    {
        animator.SetTrigger("LoadDrop");
        loadVisual.SetActive(true);
        onDropAudioParent.SetActive(true);
    }

    public void OnLoadImpact()
    {
        Debug.Log("On Load Impact Called");
        onImpactAudioParent.SetActive(true);
        cameraShaker.BeginShake();
    }

    // Add Event for On Load Impact here?
}
