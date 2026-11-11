using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

public class NailHitController : MonoBehaviour
{
    [SerializeField] 
    private Transform nail;
    [SerializeField] 
    private Vector3 localDriveDirection = Vector3.down;

    [SerializeField] 
    private float distancePerHit = 0.02f;
    [SerializeField] 
    private int hitsRequired = 3;

    [SerializeField]
    private float minimumHitSpeed = 1.0f;

    [SerializeField]
    private AudioSource nailAudioSource;

    private int currentHits;
    private bool completed;
    private bool canHit = true;

    [SerializeField]
    private float hitCooldown = .15f;
    private float lastHitTime;

    public UnityEvent onNailCompleted;

    private void OnTriggerEnter(Collider collision)
    {
        Debug.Log(gameObject.name + " OnCollisonEnter()");

        if (completed)
        {
            Debug.Log(gameObject.name + " Is completed being nailed in");
            return;
        }


        if (!collision.CompareTag("HammerHead"))
        {
            Debug.Log(gameObject.name + " got hit by collider that wasn't HammerHead");
            return;
        }

        if (Time.time - lastHitTime < hitCooldown)
            return;

        lastHitTime = Time.time;

        RegisterHit();
    }

    private void OnTriggerExit(Collider other)
    {
        canHit = true;
    }

    private void RegisterHit()
    {
        canHit = false;

        currentHits++;

        nail.localPosition += localDriveDirection.normalized * distancePerHit;

        nailAudioSource.Play();

        if (currentHits >= hitsRequired)
        {
            completed = true;
            NailCompleted();
        }
    }

    private void NailCompleted()
    {
        Debug.Log("Nail fully hammered");
        onNailCompleted.Invoke();
    }
}