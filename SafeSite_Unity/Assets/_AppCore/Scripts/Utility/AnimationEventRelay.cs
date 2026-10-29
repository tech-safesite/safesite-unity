using UnityEngine;
using UnityEngine.Events;

namespace SafeSite.Core.Utilities
{
    public class AnimationEventRelay : MonoBehaviour
    {
        [SerializeField]
        private UnityEvent onAnimationEvent;

        public void OnAnimationEventTrigger()
        {
            onAnimationEvent?.Invoke();
        }

    }
}