using UnityEngine;
using UnityEngine.Events;

public class AnimationEventRelay : MonoBehaviour
{
    [SerializeField]
    private UnityEvent onAnimationEvent;

    public void OnAnimationEventTrigger()
    {
        onAnimationEvent?.Invoke();
    }

}
