using UnityEngine;
using UnityEngine.Animations;

public class PlayerLookAtConstraint : MonoBehaviour
{
    private LookAtConstraint lookAtConstraint;

    [SerializeField]
    private Vector3 rotationOffset;

    private void Awake()
    {
        lookAtConstraint = GetComponent<LookAtConstraint>();

        if (lookAtConstraint == null)
        {
            lookAtConstraint = gameObject.AddComponent<LookAtConstraint>();
        }
    }

    private void Start()
    {
        ConstraintSource source = new ConstraintSource
        {
            sourceTransform = Camera.main.transform,
            weight = 1f,
        };

        lookAtConstraint.AddSource(source);
        lookAtConstraint.constraintActive = true;
        lookAtConstraint.rotationOffset = rotationOffset;
    }
}
