using UnityEngine;

public class FollowUserCenter : MonoBehaviour
{
    [SerializeField]
    private Transform head;

    [SerializeField]
    private Vector3 localOffset;

    [SerializeField]
    private float positionSmoothSpeed = 8f;
    [SerializeField] 
    private float rotationSmoothSpeed = 8f;


    [SerializeField]
    private bool ignoreRotation  = false;


    private void Awake()
    {
        if (head == null)
        {
            head = Camera.main.transform;
        }    
    }

    private void LateUpdate()
    {
        if (head == null)
            return;

        Vector3 forward = head.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
            return;

        forward.Normalize();

        Quaternion targetRotation = Quaternion.LookRotation(forward, Vector3.up);

        Vector3 targetPosition =
            head.position + targetRotation * localOffset;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            1f - Mathf.Exp(-positionSmoothSpeed * Time.deltaTime)
        );

        if(ignoreRotation)
        {
            return;
        }
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            1f - Mathf.Exp(-rotationSmoothSpeed * Time.deltaTime)
        );
    }
}