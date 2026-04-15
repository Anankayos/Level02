using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    // ══════════════════════════════════════════════════════════
    // INSPECTOR
    // ══════════════════════════════════════════════════════════

    [Header("Health")]
    [Tooltip("Maximum HP. Bullets deal 15 dmg → ~7 hits to die.")]
    public float maxHealth = 100f;

    // ── Fall Damage ───────────────────────────────────────────
    [Header("Fall Damage")]
    [Tooltip("Minimum vertical impact speed (m/s) before any fall damage is dealt. " +
             "Starter Assets character reaches ~10 m/s after ~5 m drop.")]
    public float minFallSpeed = 10f;

    [Tooltip("Vertical impact speed (m/s) at which maxFallDamage is fully applied.")]
    public float maxFallSpeed = 22f;

    [Tooltip("Maximum damage from a single fall (reached at maxFallSpeed). " +
             "100 = insta-kill at terminal velocity.")]
    public float maxFallDamage = 100f;

    [Tooltip("Global multiplier on all fall damage. 1 = normal. 0 = disable fall damage.")]
    public float fallDamageMultiplier = 1f;

    [Tooltip("Seconds of grace period after landing before another fall can deal damage. " +
             "Prevents micro-bounce double-hits on slopes.")]
    public float fallDamageCooldown = 0.3f;

    // ── Respawn ───────────────────────────────────────────────
    [Header("Respawn")]
    [Tooltip("Seconds between death and respawn.")]
    public float respawnDelay = 3f;

    [Tooltip("Teleport destination on respawn. If null, CheckpointManager handles it.")]
    public Transform respawnPoint;

    // ── UI ────────────────────────────────────────────────────
    [Header("UI (optional)")]
    public Slider     healthBar;
    public CanvasGroup deathScreenCanvasGroup;
    public Image      damageFlashImage;

    [Header("Death Screen Text (optional)")]
    public Text   deathMessageText;
    public string deathMessage = "YOU DIED";

    // ── Events ────────────────────────────────────────────────
    [Header("Events")]
    [Tooltip("Fires every time health changes. Passes normalised 0-1 value.")]
    public UnityEvent<float> OnHealthChanged;

    [Tooltip("Fires once on death.")]
    public UnityEvent OnDied;

    [Tooltip("Fires when the respawn sequence finishes.")]
    public UnityEvent OnRespawned;

    // ── Component References ──────────────────────────────────
    [Header("Component References")]
    [Tooltip("Auto-found if left empty.")]
    public Animator          playerAnimator;
    public CharacterController characterController;

    // Auto-resolved in Awake — no Inspector assignment needed
    private StarterAssets.ThirdPersonController playerMovementComponent;
    private PlayerCombat playerCombatComponent;

    // ── FX ────────────────────────────────────────────────────
    [Header("FX (optional)")]
    public GameObject deathVFXPrefab;

    // ══════════════════════════════════════════════════════════
    // PUBLIC READ-ONLY STATE
    // ══════════════════════════════════════════════════════════

    public float CurrentHealth { get; private set; }

    /// <summary>Used by MedikitPickup and health-bar UI.</summary>
    public float MaxHealth => maxHealth;

    /// <summary>True during the death + respawn sequence.</summary>
    public bool IsDead { get; private set; }

    // ══════════════════════════════════════════════════════════
    // PRIVATE RUNTIME
    // ══════════════════════════════════════════════════════════

    // Fall tracking
    private bool  _wasGrounded;
    private float _peakFallVelocity;   // most-negative Y velocity seen while airborne
    private float _fallDamageCooldownTimer;

    // Animator hashes (cached — faster than string lookup each frame)
    private static readonly int HashDie     = Animator.StringToHash("Die");
    private static readonly int HashHit     = Animator.StringToHash("Hit");
    private static readonly int HashIsDead  = Animator.StringToHash("IsDead");
    private static readonly int HashRespawn = Animator.StringToHash("Respawn");

    // ══════════════════════════════════════════════════════════
    // UNITY MESSAGES
    // ══════════════════════════════════════════════════════════

    void Awake()
    {
        if (playerAnimator      == null) playerAnimator      = GetComponent<Animator>();
        if (characterController == null) characterController = GetComponent<CharacterController>();
        playerMovementComponent = GetComponent<StarterAssets.ThirdPersonController>();
        playerCombatComponent   = GetComponent<PlayerCombat>();

        CurrentHealth = maxHealth;
        IsDead        = false;

        RefreshHealthUI(1f);
        EnsureDeathScreenHidden();
    }

    void Update()
    {
        if (IsDead) return;
        TrackFallDamage();
    }

    // ══════════════════════════════════════════════════════════
    // FALL DAMAGE
    // ══════════════════════════════════════════════════════════

    void TrackFallDamage()
    {
        if (characterController == null) return;

        bool isGrounded = characterController.isGrounded;

        // Cool-down timer (prevents slope-bounce double hits)
        if (_fallDamageCooldownTimer > 0f)
            _fallDamageCooldownTimer -= Time.deltaTime;

        if (!isGrounded)
        {
            // While airborne, track the fastest downward velocity
            float vy = characterController.velocity.y;
            if (vy < _peakFallVelocity)
                _peakFallVelocity = vy;
        }

        // ── Landing moment: airborne → grounded ──────────────
        if (!_wasGrounded && isGrounded && _fallDamageCooldownTimer <= 0f)
        {
            float impactSpeed = Mathf.Abs(_peakFallVelocity);

            if (impactSpeed > minFallSpeed && fallDamageMultiplier > 0f)
            {
                // Linear interpolation between thresholds:
                //   impactSpeed == minFallSpeed → 0 damage
                //   impactSpeed == maxFallSpeed → maxFallDamage
                float t      = Mathf.InverseLerp(minFallSpeed, maxFallSpeed, impactSpeed);
                float damage = Mathf.Lerp(0f, maxFallDamage, t) * fallDamageMultiplier;

                TakeDamage(damage);
            }

            // Reset regardless, so partial falls don't accumulate
            _peakFallVelocity        = 0f;
            _fallDamageCooldownTimer = fallDamageCooldown;
        }

        // ── Reset peak when freshly airborne ─────────────────
        if (_wasGrounded && !isGrounded)
            _peakFallVelocity = 0f;

        _wasGrounded = isGrounded;
    }

    // ══════════════════════════════════════════════════════════
    // PUBLIC API
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Apply damage from any source (bullets = 15, melee = 22, fall = variable).
    /// Signature matches IDamageable(float, GameObject source = null).
    /// </summary>
    public void TakeDamage(float amount, GameObject source = null)
    {
        if (IsDead)    return;
        if (amount <= 0f) return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        float normalised = CurrentHealth / maxHealth;

        OnHealthChanged?.Invoke(normalised);
        RefreshHealthUI(normalised);

        if (damageFlashImage != null)
            StartCoroutine(FlashDamageVFX());

        playerAnimator?.SetTrigger(HashHit);

        if (CurrentHealth <= 0f)
            Die();
    }

    /// <summary>Restore HP (medikit, etc.).</summary>
    public void Heal(float amount)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
        float normalised = CurrentHealth / maxHealth;
        OnHealthChanged?.Invoke(normalised);
        RefreshHealthUI(normalised);
    }

    /// <summary>
    /// Set HP to an exact value.
    /// Used by CheckpointManager when restoring a saved checkpoint.
    /// </summary>
    public void SetHealth(float value)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Clamp(value, 0f, maxHealth);
        float normalised = CurrentHealth / maxHealth;
        OnHealthChanged?.Invoke(normalised);
        RefreshHealthUI(normalised);
    }

    // ══════════════════════════════════════════════════════════
    // DEATH
    // ══════════════════════════════════════════════════════════

    void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Disable controls
        if (characterController     != null) characterController.enabled     = false;
        if (playerMovementComponent != null) playerMovementComponent.enabled = false;
        if (playerCombatComponent   != null) playerCombatComponent.enabled   = false;

        if (playerAnimator != null)
        {
            playerAnimator.SetBool(HashIsDead, true);
            playerAnimator.SetTrigger(HashDie);
        }

        if (deathVFXPrefab != null)
            Instantiate(deathVFXPrefab, transform.position, Quaternion.identity);

        OnDied?.Invoke();
        StartCoroutine(DeathRespawnSequence());
    }

    IEnumerator DeathRespawnSequence()
    {
        // Fade in death screen
        if (deathScreenCanvasGroup != null)
        {
            deathScreenCanvasGroup.gameObject.SetActive(true);
            if (deathMessageText != null) deathMessageText.text = deathMessage;

            float e = 0f;
            while (e < 1f)
            {
                e += Time.deltaTime * 1.5f;
                deathScreenCanvasGroup.alpha = Mathf.Clamp01(e);
                yield return null;
            }
        }

        yield return new WaitForSeconds(respawnDelay);
        DoRespawn();
    }

    // ══════════════════════════════════════════════════════════
    // RESPAWN
    // ══════════════════════════════════════════════════════════

    /// <summary>Force an immediate respawn (called by CheckpointManager or after delay).</summary>
    public void DoRespawn()
    {
        IsDead        = false;
        CurrentHealth = maxHealth;

        // Reset fall state so landing after respawn doesn't deal damage
        _peakFallVelocity        = 0f;
        _wasGrounded             = true;
        _fallDamageCooldownTimer = fallDamageCooldown;

        // CheckpointManager owns scene + position reset
        CheckpointManager cm = CheckpointManager.Instance;
        if (cm != null)
        {
            RestorePlayerControls();
            cm.LoadCheckpoint();
            return;
        }

        // Manual fallback
        if (respawnPoint != null)
        {
            if (characterController != null) characterController.enabled = false;
            transform.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
            if (characterController != null) characterController.enabled = true;
        }

        RestorePlayerControls();

        // Reset weapon if player shouldn't keep it through death
        PlayerCombat combat = GetComponent<PlayerCombat>();
        if (combat != null && !combat.keepWeaponOnRespawn)
            combat.UnequipWeapon();

        if (playerAnimator != null)
        {
            playerAnimator.SetBool(HashIsDead, false);
            playerAnimator.SetTrigger(HashRespawn);
        }

        OnHealthChanged?.Invoke(1f);
        RefreshHealthUI(1f);
        OnRespawned?.Invoke();
        StartCoroutine(FadeOutDeathScreen());
    }

    void RestorePlayerControls()
    {
        if (characterController     != null) characterController.enabled     = true;
        if (playerMovementComponent != null) playerMovementComponent.enabled = true;
        if (playerCombatComponent   != null) playerCombatComponent.enabled   = true;
    }

    IEnumerator FadeOutDeathScreen()
    {
        if (deathScreenCanvasGroup == null) yield break;
        yield return new WaitForSeconds(0.3f);

        float t = 1f;
        while (t > 0f)
        {
            t -= Time.deltaTime * 2f;
            deathScreenCanvasGroup.alpha = Mathf.Clamp01(t);
            yield return null;
        }
        deathScreenCanvasGroup.gameObject.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════
    // UI HELPERS
    // ══════════════════════════════════════════════════════════

    void RefreshHealthUI(float normalised)
    {
        if (healthBar != null)
            healthBar.value = normalised;
    }

    void EnsureDeathScreenHidden()
    {
        if (deathScreenCanvasGroup == null) return;
        deathScreenCanvasGroup.alpha = 0f;
        deathScreenCanvasGroup.gameObject.SetActive(false);
    }

    IEnumerator FlashDamageVFX()
    {
        Color c = damageFlashImage.color;
        c.a = 0.45f;
        damageFlashImage.color = c;

        float t = 0.35f;
        while (t > 0f)
        {
            t -= Time.deltaTime * 2.5f;
            c.a = Mathf.Clamp01(t);
            damageFlashImage.color = c;
            yield return null;
        }

        c.a = 0f;
        damageFlashImage.color = c;
    }
}