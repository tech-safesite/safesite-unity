using DG.Tweening;
using UnityEngine;

public class TweenOnEnable : MonoBehaviour
{
    [SerializeField] 
    private float duration = 0.25f;
    [SerializeField] 
    private float startScale = 0.9f;
    [SerializeField] 
    private Ease ease = Ease.OutBack;

    private Vector3 originalScale;

    private void Awake()
    {
        originalScale = transform.localScale;
    }

    private void OnEnable()
    {
        transform.DOKill();

        transform.localScale = originalScale * startScale;

        transform
            .DOScale(originalScale, duration)
            .SetEase(ease);
    }

    private void OnDisable()
    {
        transform.DOKill();
    }
}