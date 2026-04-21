using UnityEngine;

/// <summary>
/// Destructible prop that integrates with the checkpoint reset system.
///
/// Respawn behaviour is controlled by whether a PersistentPickup component
/// is attached to the same GameObject:
///
///   NO  PersistentPickup  →  resettable prop (crate, glass, vent...)
///                            Respawns every time the player loads a checkpoint.
///
///   YES PersistentPickup  →  one-way permanent barrier (pillar, sealed door...)
///                            Once broken it stays broken, exactly like a key pickup.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DestructibleSurface : MonoBehaviour, IDamageable, IResettable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 50f;

    [Header("Destruction")]
    [SerializeField] private GameObject destroyedVersionPrefab;
    [SerializeField] private GameObject destructionVFX;
    [SerializeField] private float      impactNoiseRadius = 6f;

    private float      _health;
    private bool       _isDestroyed;
    private GameObject _spawnedRubble;

    // Cached reference — set in Awake, never changes
    private PersistentPickup _persistent;

    // ── IResettable ───────────────────────────────────────────
    // Stable scene-path ID so save/load IDs always match.
    public string ResettableID => GetScenePath();

    private string GetScenePath()
    {
        var t = transform;
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return "DS:" + path;
    }

    public bool IsAlive => !_isDestroyed;

    // ── Unity Messages ────────────────────────────────────────

    private void Awake()
    {
        _health     = maxHealth;
        _persistent = GetComponent<PersistentPickup>();
        SaveInitialState();
    }

    // ── Damage ──────────────────────────────────────────────

    public void TakeDamage(float amount, GameObject source = null)
    {
        GameEvents.FireHit(HitType.Destructible);
        if (_isDestroyed) return;
        _health -= amount;
        NoiseEmitter.EmitNoise(transform.position, impactNoiseRadius, NoiseType.Impact);
        if (_health <= 0f) BreakApart(spawnVFX: true);
    }

    // Called silently on checkpoint restore — no VFX, no sound
    public void ForceDestroy() => BreakApart(spawnVFX: false);

    private void BreakApart(bool spawnVFX)
    {
        _isDestroyed = true;

        // Register in SceneStateTracker so CheckpointManager Phase 2 can
        // re-suppress this object if it was already broken at save time.
        SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);

        // If a PersistentPickup is attached, mark it collected too so its
        // PP: ID also lands in SceneStateTracker and Phase 2 suppresses it
        // via both IResettable components.
        _persistent?.Collect();

        if (spawnVFX && destructionVFX)
            Instantiate(destructionVFX, transform.position, transform.rotation);

        if (destroyedVersionPrefab)
            _spawnedRubble = Instantiate(destroyedVersionPrefab, transform.position, transform.rotation);

        gameObject.SetActive(false);
    }

    // ── IResettable ───────────────────────────────────────────

    public void SaveInitialState() { }

    public void ResetState()
    {
        // Persistent surfaces (PersistentPickup attached) rely entirely on
        // Phase 2 ID matching to stay suppressed — we still re-enable here
        // so Phase 2 gets a live object to work with.
        _isDestroyed = false;
        _health      = maxHealth;

        if (_spawnedRubble)
        {
            Destroy(_spawnedRubble);
            _spawnedRubble = null;
        }

        gameObject.SetActive(true);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, GetComponent<Renderer>()?.bounds.size ?? Vector3.one);
    }
}
