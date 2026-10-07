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

    [SerializeField]
    private bool loadDropped = false;

    [SerializeField]
    private GameObject loadDebris;

    [SerializeField]
    private GameObject exclusionZone;

    private void Start()
    {
        loadVisual.SetActive(false);
        loadDebris.SetActive(false);

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
        loadDebris.SetActive(true);
        loadDropped = true;
    }

    public bool GetLoadDropped()
    {
        return loadDropped;
    }

    public void ResetLoad()
    {
        // Reset Load Position. 
        animator.SetTrigger("ResetLoad");
        loadDebris.SetActive(false);
        // Remove Any Debris etc
    }

    public void ShowExclusionZone()
    {
        exclusionZone.SetActive(true);
    }

    // Add Event for On Load Impact here?
}
