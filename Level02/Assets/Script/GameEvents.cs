using System;

public static class GameEvents
{
    // ── Player Health ─────────────────────────────────────────
    public static event Action<int, int> OnHealthChanged;
    public static event Action           OnPlayerDeath;
    public static event Action           OnDamageReceived;

    public static void FireHealthChanged(int cur, int max) => OnHealthChanged?.Invoke(cur, max);
    public static void FirePlayerDeath()                   => OnPlayerDeath?.Invoke();
    public static void FireDamageReceived()                => OnDamageReceived?.Invoke();

    // ── Ammo ──────────────────────────────────────────────────
    public static event Action<int, int> OnAmmoChanged;
    public static event Action<string>   OnWeaponEquipped;
    public static event Action           OnWeaponUnequipped;

    public static void FireAmmoChanged(int mag, int res) => OnAmmoChanged?.Invoke(mag, res);
    public static void FireWeaponEquipped(string name)   => OnWeaponEquipped?.Invoke(name);
    public static void FireWeaponUnequipped()            => OnWeaponUnequipped?.Invoke();

    // ── Enemy Alert ───────────────────────────────────────────
    public static event Action<EnemyAlertLevel> OnEnemyAlertChanged;
    public static void FireEnemyAlertChanged(EnemyAlertLevel level)
        => OnEnemyAlertChanged?.Invoke(level);

    // ── Checkpoint ────────────────────────────────────────────
    public static event Action<string> OnCheckpointReached;
    public static void FireCheckpointReached(string msg = "Checkpoint Saved")
        => OnCheckpointReached?.Invoke(msg);

    // ── Interaction Prompt ────────────────────────────────────
    public static event Action<string> OnShowInteraction;
    public static event Action         OnHideInteraction;
    public static void FireShowInteraction(string msg) => OnShowInteraction?.Invoke(msg);
    public static void FireHideInteraction()           => OnHideInteraction?.Invoke();

    // ── Hit Marker ────────────────────────────────────────────
    public static event Action<HitType> OnHit;
    public static void FireHit(HitType type) => OnHit?.Invoke(type);

    // ── Tutorial ──────────────────────────────────────────────
    public static event Action<string, string, float> OnShowTutorial;
    public static event Action                        OnHideTutorial;

    public static void FireShowTutorial(string message, string inputHint = "", float duration = 4f)
        => OnShowTutorial?.Invoke(message, inputHint, duration);
    public static void FireHideTutorial()
        => OnHideTutorial?.Invoke();

    // ── Stealth Kill ──────────────────────────────────────────
    public static event Action OnStealthKill;
    public static void FireStealthKill() => OnStealthKill?.Invoke();

    // ── Stealth Kill Prompt ───────────────────────────────────
    public static event Action OnShowStealthPrompt;
    public static event Action OnHideStealthPrompt;

    public static void FireShowStealthPrompt() => OnShowStealthPrompt?.Invoke();
    public static void FireHideStealthPrompt() => OnHideStealthPrompt?.Invoke();
}

// ── Standalone enums ──────────────────────────────────────────
public enum EnemyAlertLevel { None, Suspicious, Alerted }