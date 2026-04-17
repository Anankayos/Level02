using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class PlayerHUD : MonoBehaviour
{
    // ── Health ────────────────────────────────────────────────────
    [Header("Health")]
    [SerializeField] private Slider          healthSlider;
    [SerializeField] private Image           healthFill;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Image           damageVignette;
    [SerializeField] private Image           lowHealthPulse;

    [Header("Health Colors")]
    [SerializeField] private Color healthFull         = new Color(0.2f, 0.85f, 0.3f);
    [SerializeField] private Color healthLow          = new Color(0.9f, 0.15f, 0.15f);
    [SerializeField] private float lowHealthThreshold = 0.35f;

    // ── Ammo ──────────────────────────────────────────────────────
    [Header("Ammo")]
    [SerializeField] private GameObject        ammoPanel;
    [SerializeField] private TextMeshProUGUI   ammoMagText;
    [SerializeField] private TextMeshProUGUI   ammoReserveText;
    [SerializeField] private TextMeshProUGUI   weaponNameText;

    // ── Alert ─────────────────────────────────────────────────────
    [Header("Alert")]
    [SerializeField] private GameObject        alertPanel;
    [SerializeField] private Image             alertIcon;
    [SerializeField] private TextMeshProUGUI   alertText;

    private readonly Color colorSuspicious = new Color(1f, 0.85f, 0f);
    private readonly Color colorAlerted    = new Color(1f, 0.2f,  0.1f);

    // ── Checkpoint ────────────────────────────────────────────────
    [Header("Checkpoint Toast")]
    [SerializeField] private CanvasGroup     checkpointGroup;
    [SerializeField] private TextMeshProUGUI checkpointText;

    // ── Interaction ───────────────────────────────────────────────
    [Header("Interaction Prompt")]
    [SerializeField] private GameObject      interactionPanel;
    [SerializeField] private TextMeshProUGUI interactionText;

    // ── Hit Marker ────────────────────────────────────────────────
    [Header("Hit Marker")]
    [SerializeField] private HitMarkerController hitMarkerController;

    // ── Stealth Kill Banner ───────────────────────────────────────
    [Header("Stealth Kill")]
    [SerializeField] private CanvasGroup     stealthKillGroup;
    [SerializeField] private TextMeshProUGUI stealthKillText;

    // ── Stealth Kill Prompt ───────────────────────────────────────
    [Header("Stealth Prompt")]
    [SerializeField] private CanvasGroup stealthPromptGroup;

    // ── Runtime ───────────────────────────────────────────────────
    private Coroutine _damageFlashCo;
    private Coroutine _checkpointCo;
    private Coroutine _stealthKillCo;
    private Coroutine _stealthPromptCo;
    private bool      _isLowHealthPulsing;


    // ═════════════════════════════════════════════════════════════
    #region Unity Lifecycle

    private void OnEnable()
    {
        GameEvents.OnHealthChanged      += HandleHealthChanged;
        GameEvents.OnDamageReceived     += HandleDamageReceived;
        GameEvents.OnPlayerDeath        += HandlePlayerDeath;
        GameEvents.OnAmmoChanged        += HandleAmmoChanged;
        GameEvents.OnWeaponEquipped     += HandleWeaponEquipped;
        GameEvents.OnWeaponUnequipped   += HandleWeaponUnequipped;
        GameEvents.OnEnemyAlertChanged  += HandleAlertChanged;
        GameEvents.OnCheckpointReached  += HandleCheckpointReached;
        GameEvents.OnShowInteraction    += ShowInteraction;
        GameEvents.OnHideInteraction    += HideInteraction;
        GameEvents.OnHit                += HandleHit;
        GameEvents.OnStealthKill        += HandleStealthKill;
        GameEvents.OnShowStealthPrompt  += HandleShowStealthPrompt;
        GameEvents.OnHideStealthPrompt  += HandleHideStealthPrompt;
    }

    private void OnDisable()
    {
        GameEvents.OnHealthChanged      -= HandleHealthChanged;
        GameEvents.OnDamageReceived     -= HandleDamageReceived;
        GameEvents.OnPlayerDeath        -= HandlePlayerDeath;
        GameEvents.OnAmmoChanged        -= HandleAmmoChanged;
        GameEvents.OnWeaponEquipped     -= HandleWeaponEquipped;
        GameEvents.OnWeaponUnequipped   -= HandleWeaponUnequipped;
        GameEvents.OnEnemyAlertChanged  -= HandleAlertChanged;
        GameEvents.OnCheckpointReached  -= HandleCheckpointReached;
        GameEvents.OnShowInteraction    -= ShowInteraction;
        GameEvents.OnHideInteraction    -= HideInteraction;
        GameEvents.OnHit                -= HandleHit;
        GameEvents.OnStealthKill        -= HandleStealthKill;
        GameEvents.OnShowStealthPrompt  -= HandleShowStealthPrompt;
        GameEvents.OnHideStealthPrompt  -= HandleHideStealthPrompt;
    }

    private void Start()
    {
        damageVignette.gameObject.SetActive(false);
        lowHealthPulse.gameObject.SetActive(false);
        alertPanel.SetActive(false);
        interactionPanel.SetActive(false);
        ammoPanel.SetActive(false);

        if (checkpointGroup    != null) checkpointGroup.alpha    = 0f;
        if (stealthKillGroup   != null) { stealthKillGroup.alpha = 0f; stealthKillGroup.gameObject.SetActive(false); }
        if (stealthPromptGroup != null) { stealthPromptGroup.alpha = 0f; stealthPromptGroup.gameObject.SetActive(false); }

        // Force-refresh from current player state
        var ph = Object.FindFirstObjectByType<PlayerHealth>();
        if (ph != null) HandleHealthChanged((int)ph.CurrentHealth, (int)ph.MaxHealth);

        var pc = Object.FindFirstObjectByType<PlayerCombat>();
        if (pc != null) HandleAmmoChanged(pc.CurrentAmmo, pc.ReserveAmmo);
    }

    private void Update()
    {
        if (_isLowHealthPulsing) PulseLowHealth();
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Health

    private void HandleHealthChanged(int current, int max)
    {
        float ratio         = Mathf.Clamp01((float)current / max);
        healthSlider.value  = ratio;
        healthText.text     = $"{current}";
        healthFill.color    = Color.Lerp(healthLow, healthFull, ratio);

        bool isLow = ratio <= lowHealthThreshold;
        if (isLow && !_isLowHealthPulsing)
        {
            _isLowHealthPulsing = true;
            lowHealthPulse.gameObject.SetActive(true);
        }
        else if (!isLow && _isLowHealthPulsing)
        {
            _isLowHealthPulsing = false;
            lowHealthPulse.gameObject.SetActive(false);
        }
    }

    private void PulseLowHealth()
    {
        float alpha = Mathf.Abs(Mathf.Sin(Time.time * 2.5f)) * 0.5f;
        var c = lowHealthPulse.color;
        c.a = alpha;
        lowHealthPulse.color = c;
    }

    private void HandleDamageReceived()
    {
        if (_damageFlashCo != null) StopCoroutine(_damageFlashCo);
        _damageFlashCo = StartCoroutine(DamageFlash());
    }

    private IEnumerator DamageFlash()
    {
        damageVignette.gameObject.SetActive(true);
        SetAlpha(damageVignette, 0.65f);

        float t = 0f, duration = 0.55f;
        while (t < duration)
        {
            t += Time.deltaTime;
            SetAlpha(damageVignette, Mathf.Lerp(0.65f, 0f, t / duration));
            yield return null;
        }
        damageVignette.gameObject.SetActive(false);
    }

    private void HandlePlayerDeath()
    {
        SetAlpha(damageVignette, 0.8f);
        damageVignette.gameObject.SetActive(true);
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Ammo

    private void HandleAmmoChanged(int mag, int reserve)
    {
        ammoPanel.SetActive(true);
        ammoMagText.text     = mag.ToString();
        ammoReserveText.text = $"/ {reserve}";
        ammoMagText.color    = mag == 0 ? healthLow : Color.white;
    }

    private void HandleWeaponEquipped(string weaponName)
    {
        ammoPanel.SetActive(true);
        if (weaponNameText != null)
            weaponNameText.text = weaponName.ToUpper();
    }

    private void HandleWeaponUnequipped()
    {
        ammoPanel.SetActive(false);
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Enemy Alert

    private void HandleAlertChanged(EnemyAlertLevel level)
    {
        if (level == EnemyAlertLevel.None)
        {
            alertPanel.SetActive(false);
            return;
        }

        alertPanel.SetActive(true);

        if (level == EnemyAlertLevel.Suspicious)
        {
            alertIcon.color = colorSuspicious;
            alertText.text  = "!";
            alertText.color = colorSuspicious;
        }
        else
        {
            alertIcon.color = colorAlerted;
            alertText.text  = "!! DETECTED";
            alertText.color = colorAlerted;
        }
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Checkpoint Toast

    private void HandleCheckpointReached(string msg)
    {
        if (_checkpointCo != null) StopCoroutine(_checkpointCo);
        _checkpointCo = StartCoroutine(CheckpointToast(msg));
    }

    private IEnumerator CheckpointToast(string msg)
    {
        checkpointText.text   = msg;
        checkpointGroup.alpha = 0f;
        yield return Fade(checkpointGroup, 0f, 1f, 0.35f);
        yield return new WaitForSeconds(2.2f);
        yield return Fade(checkpointGroup, 1f, 0f, 0.55f);
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Interaction Prompt

    private void ShowInteraction(string msg)
    {
        interactionPanel.SetActive(true);
        interactionText.text = msg;
    }

    private void HideInteraction()
    {
        interactionPanel.SetActive(false);
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Hit Marker

    private void HandleHit(HitType type)
    {
        hitMarkerController?.Show(type);
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Stealth Kill

    private void HandleStealthKill()
    {
        hitMarkerController?.Show(HitType.Kill);

        if (_stealthKillCo != null) StopCoroutine(_stealthKillCo);
        _stealthKillCo = StartCoroutine(StealthKillRoutine());
    }

    private IEnumerator StealthKillRoutine()
    {
        stealthKillGroup.gameObject.SetActive(true);
        yield return Fade(stealthKillGroup, 0f, 1f, 0.15f);
        yield return new WaitForSeconds(1.4f);
        yield return Fade(stealthKillGroup, 1f, 0f, 0.6f);
        stealthKillGroup.gameObject.SetActive(false);
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Stealth Prompt

    private void HandleShowStealthPrompt()
    {
        if (_stealthPromptCo != null) StopCoroutine(_stealthPromptCo);
        _stealthPromptCo = StartCoroutine(FadeStealthPrompt(0f, 1f, 0.15f));
    }

    private void HandleHideStealthPrompt()
    {
        if (_stealthPromptCo != null) StopCoroutine(_stealthPromptCo);
        _stealthPromptCo = StartCoroutine(HideStealthPromptRoutine());
    }

    private IEnumerator FadeStealthPrompt(float from, float to, float duration)
    {
        stealthPromptGroup.gameObject.SetActive(true);
        yield return Fade(stealthPromptGroup, from, to, duration);
    }

    private IEnumerator HideStealthPromptRoutine()
    {
        yield return Fade(stealthPromptGroup, stealthPromptGroup.alpha, 0f, 0.2f);
        stealthPromptGroup.gameObject.SetActive(false);
    }

    #endregion


    // ═════════════════════════════════════════════════════════════
    #region Helpers

    private static void SetAlpha(Graphic g, float a)
    {
        var c = g.color; c.a = a; g.color = c;
    }

    private static IEnumerator Fade(CanvasGroup cg, float from, float to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t        += Time.deltaTime;
            cg.alpha  = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        cg.alpha = to;
    }

    #endregion
}