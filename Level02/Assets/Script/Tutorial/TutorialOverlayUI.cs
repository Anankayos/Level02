using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class TutorialOverlayUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CanvasGroup overlayRoot;
    [SerializeField] private Image backdrop;
    [SerializeField] private Image imageHolder;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private Image holdRadial;
    [SerializeField] private TextMeshProUGUI holdLabel;

    [Header("Input")]
    [SerializeField] private InputActionReference holdToCloseAction;

    [Header("Settings")]
    [SerializeField] private float backdropAlpha = 0.65f;
    [SerializeField] private float fadeDuration = 0.25f;

    private float _holdDuration;
    private float _holdProgress;
    private bool _isVisible;
    private Coroutine _fadeCoroutine;

    private void Awake()
    {
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (holdToCloseAction != null)
            holdToCloseAction.action.Enable();
    }

    private void OnDisable()
    {
        if (holdToCloseAction != null)
            holdToCloseAction.action.Disable();
    }

    private void Update()
    {
        if (!_isVisible || holdToCloseAction == null) return;

        bool holding = holdToCloseAction.action.IsPressed();

        if (holding)
        {
            _holdProgress += Time.unscaledDeltaTime;
            if (holdRadial != null) holdRadial.fillAmount = _holdProgress / _holdDuration;
            if (holdLabel != null) holdLabel.text = "Hold to close";
            if (_holdProgress >= _holdDuration) Hide();
        }
        else
        {
            _holdProgress = Mathf.Max(0f, _holdProgress - Time.unscaledDeltaTime * 1.5f);
            if (holdRadial != null) holdRadial.fillAmount = _holdProgress / _holdDuration;
        }
    }

    public void Show(TutorialData data)
    {
        Debug.Log("[TutorialOverlayUI] Show() called. Active before: " + gameObject.activeSelf);

        gameObject.SetActive(true);

        Debug.Log("[TutorialOverlayUI] Active after SetActive: " + gameObject.activeSelf);

        _holdDuration = data.holdDuration;
        _holdProgress = 0f;
        _isVisible = false;

        bodyText.text = data.bodyText;

        if (imageHolder != null)
        {
            imageHolder.sprite = data.image;
            imageHolder.enabled = data.image != null;
        }

        if (holdRadial != null) holdRadial.fillAmount = 0f;

        overlayRoot.alpha = 0f;
        overlayRoot.interactable = true;
        overlayRoot.blocksRaycasts = true;
        SetBackdropAlpha(0f);

        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeIn());

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("[TutorialOverlayUI] Show() completed. timeScale=" + Time.timeScale);
    }

    public void Hide()
    {
        if (!gameObject.activeSelf) return;
        if (_fadeCoroutine != null) StopCoroutine(_fadeCoroutine);
        _fadeCoroutine = StartCoroutine(FadeOut());
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private IEnumerator FadeIn()
    {
        Debug.Log("[TutorialOverlayUI] FadeIn started.");
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            float t = elapsed / fadeDuration;
            overlayRoot.alpha = t;
            SetBackdropAlpha(t * backdropAlpha);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        overlayRoot.alpha = 1f;
        SetBackdropAlpha(backdropAlpha);
        _isVisible = true;
        Debug.Log("[TutorialOverlayUI] FadeIn complete. Overlay visible.");
    }

    private IEnumerator FadeOut()
    {
        _isVisible = false;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            float t = 1f - (elapsed / fadeDuration);
            overlayRoot.alpha = t;
            SetBackdropAlpha(t * backdropAlpha);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        overlayRoot.alpha = 0f;
        SetBackdropAlpha(0f);
        overlayRoot.interactable = false;
        overlayRoot.blocksRaycasts = false;
        if (holdRadial != null) holdRadial.fillAmount = 0f;
        _holdProgress = 0f;
        gameObject.SetActive(false);
    }

    private void SetBackdropAlpha(float a)
    {
        if (backdrop == null) return;
        Color c = backdrop.color;
        c.a = a;
        backdrop.color = c;
    }
}
