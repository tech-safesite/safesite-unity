using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

public class TweenOnHover : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    [SerializeField] private float hoverScale = 1.05f;
    [SerializeField] private float duration = 0.15f;
    [SerializeField] private Ease ease = Ease.OutQuad;

    private Vector3 originalScale;

    private void Awake()
    {
        originalScale = transform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.DOKill();

        transform
            .DOScale(originalScale * hoverScale, duration)
            .SetEase(ease);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.DOKill();

        transform
            .DOScale(originalScale, duration)
            .SetEase(ease);
    }

    private void OnDisable()
    {
        transform.DOKill();
        transform.localScale = originalScale;
    }
}