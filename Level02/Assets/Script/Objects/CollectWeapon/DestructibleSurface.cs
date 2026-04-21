using UnityEngine;

/// <summary>
/// Destructible prop integrated with the checkpoint reset system.
///
/// Respawn behaviour is controlled by whether a PersistentPickup is attached:
///   NO  PersistentPickup  →  resettable prop  →  respawns on checkpoint reload
///   YES PersistentPickup  →  permanent barrier →  stays broken forever
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
    private PersistentPickup _persistent;

    // ── IResettable ───────────────────────────────────────────
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

    private void BreakApart(bool spawnVFX)
    {
        _isDestroyed = true;

        // Register DS: path so Phase 2 can match this object
        SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);

        // If permanent, also register PP: path via PersistentPickup
        _persistent?.Collect();

        if (spawnVFX && destructionVFX)
            Instantiate(destructionVFX, transform.position, transform.rotation);

        if (destroyedVersionPrefab)
            _spawnedRubble = Instantiate(destroyedVersionPrefab, transform.position, transform.rotation);

        gameObject.SetActive(false);
    }

    /// <summary>
    /// Silent re-suppress called by CheckpointManager Phase 2.
    /// Restores destroyed visual state WITHOUT touching SceneStateTracker.
    /// </summary>
    public void Suppress()
    {
        _isDestroyed = true;

        if (_spawnedRubble == null && destroyedVersionPrefab)
            _spawnedRubble = Instantiate(destroyedVersionPrefab, transform.position, transform.rotation);

        _persistent?.Suppress();
        gameObject.SetActive(false);
    }

    // Legacy path kept for any existing callers outside LoadRoutine
    public void ForceDestroy() => Suppress();

    // ── IResettable ───────────────────────────────────────────

    public void SaveInitialState() { }

    public void ResetState()
    {
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
