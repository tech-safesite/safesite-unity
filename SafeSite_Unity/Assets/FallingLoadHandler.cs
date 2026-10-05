using UnityEngine;

public class FallingLoadHandler : MonoBehaviour
{
    [SerializeField]
    private Animator animator;

    public void DropLoad()
    {
        animator.SetTrigger("LoadDrop");
    }

    public void OnLoadImpact()
    {

    }

    // Add Event for On Load Impact here?
}
