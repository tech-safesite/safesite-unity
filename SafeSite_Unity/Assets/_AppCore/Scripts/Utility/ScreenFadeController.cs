using System;
using System.Collections;
using UnityEngine;

namespace SafeSite.Core.UI
{


    public class ScreenFadeController : MonoBehaviour
    {
        public static ScreenFadeController Instance { get; private set; }

        [SerializeField]
        private CanvasGroup faderCanvasGroup;

        private Coroutine currentFadeRoutine;
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(Instance);
            }

            Instance = this;
        }

        private void Start()
        {
        }

        public void FadeToBlack(float fadeDuration)
        {
            currentFadeRoutine = StartCoroutine(FadeCanvas(1, fadeDuration));
            Debug.Log("Fade To Black Called");

        }

        public void FadeOutOfBlack(float fadeDuration)
        {
            Debug.Log("Fade Out Called");
           currentFadeRoutine = StartCoroutine(FadeCanvas(0, fadeDuration));
        }

        private IEnumerator FadeCanvas(float alphaTarget, float fadeDuration)
        {
            Debug.Log("Fade Canvas()");

            float startAlpha = faderCanvasGroup.alpha;
            float timeElapsed = 0f;

            if (timeElapsed >= fadeDuration) // if exceeded time
            {
                Debug.Log("Time Elapsed Higher. End Loop()");
                faderCanvasGroup.alpha = alphaTarget;
                currentFadeRoutine = null;
                yield break;
            }

            while (timeElapsed < fadeDuration)
            {
                Debug.Log("Time Elapsed Lower, Fading Time" + timeElapsed);

                timeElapsed += Time.deltaTime;

                float value = Mathf.Lerp(startAlpha, alphaTarget, Mathf.Clamp01((timeElapsed / fadeDuration)));


                faderCanvasGroup.alpha =
                Mathf.Lerp(startAlpha, alphaTarget, Mathf.Clamp01((timeElapsed / fadeDuration)));

                Debug.Log("Alpha canvas should now be " + value);
                yield return null;

            }
            faderCanvasGroup.alpha = alphaTarget;
        }

        public void EventTransition(Action action)
        {
            StartCoroutine(EventTransitionRoutine(action));
        }

        private IEnumerator EventTransitionRoutine(Action whileTransitioning, float fadeOutTime = .5f, float fadeInTime = .5f, float transitionHoldTime = 2f)
        {
            yield return FadeCanvas(1, (fadeOutTime));

            whileTransitioning.Invoke();
            yield return new WaitForSeconds(transitionHoldTime);



            yield return FadeCanvas(0, (fadeInTime));

        }
    }
}
