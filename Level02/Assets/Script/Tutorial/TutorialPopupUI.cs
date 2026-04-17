using System.Collections;
using UnityEngine;
using TMPro;

public class TutorialPopupUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform popupRoot;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI bodyText;

    [Header("Animation")]
    [SerializeField] private float slideDuration = 0.35f;
    [SerializeField] private float offscreenY = 200f;

    private Vector2 _onscreenPos;
    private Coroutine _anim;
    private Coroutine _autoHide;

    private void Awake()
    {
        _onscreenPos = popupRoot.anchoredPosition;
        popupRoot.anchoredPosition = new Vector2(_onscreenPos.x, _onscreenPos.y + offscreenY);
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        gameObject.SetActive(false);
    }

    public void Show(TutorialData data)
    {
        gameObject.SetActive(true);
        bodyText.text = data.bodyText;

        if (_anim != null) StopCoroutine(_anim);
        if (_autoHide != null) StopCoroutine(_autoHide);

        _anim = StartCoroutine(AnimateIn());

        if (data.autoHideAfter > 0f)
            _autoHide = StartCoroutine(AutoHide(data.autoHideAfter + slideDuration));
    }

    public void Hide()
    {
        if (!gameObject.activeSelf) return;
        if (_anim != null) StopCoroutine(_anim);
        if (_autoHide != null) StopCoroutine(_autoHide);
        _anim = StartCoroutine(AnimateOut());
    }

    private IEnumerator AnimateIn()
    {
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        float elapsed = 0f;
        Vector2 start = popupRoot.anchoredPosition;

        while (elapsed < slideDuration)
        {
            float t = Ease(elapsed / slideDuration);
            popupRoot.anchoredPosition = Vector2.LerpUnclamped(start, _onscreenPos, t);
            canvasGroup.alpha = t;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        popupRoot.anchoredPosition = _onscreenPos;
        canvasGroup.alpha = 1f;
    }

    private IEnumerator AnimateOut()
    {
        Vector2 hidden = new Vector2(_onscreenPos.x, _onscreenPos.y + offscreenY);
        Vector2 start = popupRoot.anchoredPosition;
        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < slideDuration)
        {
            float t = Ease(elapsed / slideDuration);
            popupRoot.anchoredPosition = Vector2.LerpUnclamped(start, hidden, t);
            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        popupRoot.anchoredPosition = hidden;
        gameObject.SetActive(false);
    }

    private IEnumerator AutoHide(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        Hide();
    }

    private static float Ease(float t)
    {
        return 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
    }
}
