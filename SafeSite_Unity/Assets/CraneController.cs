using UnityEngine;

public class CraneController : MonoBehaviour
{
    [SerializeField]
    private Animator craneAnimator;

    [Header("Crane Audio")]
    [SerializeField]
    private AudioSource craneAudioSource;

    [SerializeField]
    private AudioClip[] craneAudio;

    public void MoveCraneOnStart()
    {
        craneAnimator.SetTrigger("CraneStartMovement");
        craneAudioSource.clip = craneAudio[0];
        craneAudioSource.Play();
    }

    public void CraneAccidentMovement()
    {
        craneAnimator.SetTrigger("CraneSlowMovement");
        craneAudioSource.clip = craneAudio[1];
        craneAudioSource.Play();
    }
    public void CraneLoadCross()
    {
        craneAnimator.SetTrigger("CraneCross");
        craneAudioSource.clip = craneAudio[1];
        craneAudioSource.Play();


    }
    public void CraneLoadAtPoint()
    {
        craneAnimator.SetTrigger("CraneLoadAtPoint");
        craneAudioSource.clip = craneAudio[1];
        craneAudioSource.Play();

    }
}
