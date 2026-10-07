using UnityEngine;

public class BanksmanController : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    [SerializeField]
    private bool handWaveReceived = false;

    [SerializeField]
    private Animator animator;

    [SerializeField]
    private GameObject highlightObject;

    public void HighlightBanksnan(bool highlighted)
    {
        highlightObject.SetActive(highlighted);
    }

    public void TurnTowardPlayerAndWave() 
    {
        animator.SetTrigger("BanksmanTurn");
        // Temp look at constraint?
        // Turn Face Player on?
    }

    public void HandWaveReceived()
    {
        handWaveReceived = true;
    }

    public bool GetHandWave()
    {
        return handWaveReceived;
    }






}
