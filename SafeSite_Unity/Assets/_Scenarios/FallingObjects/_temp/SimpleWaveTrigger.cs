using UnityEngine;
using UnityEngine.Events;
public class SimpleWaveTrigger : MonoBehaviour // temp until we add gesture detection 
{

    public UnityEvent onWaveDetected;
    private void OnTriggerEnter(Collider other)
    {
        onWaveDetected.Invoke();
        gameObject.SetActive(false);

        if (other.CompareTag("Hand"))
            {
                onWaveDetected.Invoke();
                gameObject.SetActive(false);
            }
    }
          

}
