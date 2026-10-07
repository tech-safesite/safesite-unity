using UnityEngine;
using System.Collections;

public class CameraShaker : MonoBehaviour
{

    [SerializeField]
    private Transform target;

    [SerializeField]
    private float shakeDuration = 2f;
    [SerializeField]
    private float shakeStrength = .02f;

    private Vector3 startLocalPostion;
    private Coroutine shakeRoutine;
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    private void OnEnable()
    {
      //  BeginShake();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void BeginShake()
    {
        startLocalPostion = target.transform.localPosition;

        if (shakeRoutine ==null)
        {
            Debug.Log("CAMERA SHAKING!!");
            StartCoroutine(CameraShakeRoutine());
        }
    }

    private IEnumerator CameraShakeRoutine()
    {
        float currentTimeShaking = 0f;

        while (currentTimeShaking < shakeDuration) 
        {
            Vector3 offset = Random.insideUnitSphere * shakeStrength;

            // offset.y *= .5f;

            target.transform.localPosition = startLocalPostion + offset;
            currentTimeShaking += Time.deltaTime;
            yield return null;
        }

        target.transform.localPosition = startLocalPostion;
        shakeRoutine = null;
        Debug.Log("Camera Chilled out!!");

    }
}
