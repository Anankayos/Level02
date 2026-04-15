using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour
{
    // ── Movement ──────────────────────────────────────────────
    [Header("Movement")]
    [Tooltip("Initial speed in m/s. Enemy bullets: ~35. Player bullets: ~50")]
    public float speed = 50f;

    [Tooltip("Bullet drop acceleration (gravity multiplier). 0 = laser flat")]
    public float gravityScale = 1.2f;

    [Tooltip("Distance (m) at which the bullet is destroyed")]
    public float maxRange = 80f;

    // ── Damage Thresholds ─────────────────────────────────────
    [Header("Damage Thresholds")]
    [Tooltip("Full damage up to this distance")]
    public float fullDamageRange = 15f;

    [Tooltip("Minimum damage beyond this distance (until maxRange)")]
    public float noDamageRange = 60f;

    [Tooltip("Base damage value (player health = 100, so 15 = ~7 shots to kill)")]
    public float baseDamage = 15f;

    [Tooltip("Minimum damage at extreme range")]
    public float minDamage = 5f;

    // ── FX ────────────────────────────────────────────────────
    [Header("FX (optional)")]
    [Tooltip("Particle effect spawned at impact point")]
    public GameObject impactEffectPrefab;

    [Tooltip("Optional trail renderer for bullet trace")]
    public TrailRenderer trailRenderer;

    // ── Layer filtering ───────────────────────────────────────
    [Header("Layer Filtering")]
    [Tooltip("Layers that bullets can hit. Default = Everything except Bullet layer")]
    public LayerMask hitLayers = ~0;

    // ── Runtime (not serialized) ──────────────────────────────
    private Vector3    velocity;
    private float      distanceTraveled;
    private bool       isEnemyBullet;
    private GameObject ownerObject;
    private bool       hasHit;      // prevent double-hit

    // ─────────────────────────────────────────────────────────
    // INITIALIZATION — called immediately after Instantiate()
    // ─────────────────────────────────────────────────────────
    /// <summary>
    /// Must be called after instantiating the bullet.
    /// </summary>
    /// <param name="direction">Normalized fire direction</param>
    /// <param name="fromEnemy">True = enemy bullet → damages PlayerHealth. False = player bullet → damages IDamageable</param>
    /// <param name="owner">The GameObject that fired (used to avoid self-hit)</param>
    public void Initialize(Vector3 direction, bool fromEnemy, GameObject owner)
    {
        velocity         = direction.normalized * speed;
        isEnemyBullet    = fromEnemy;
        ownerObject      = owner;
        distanceTraveled = 0f;
        hasHit           = false;

        transform.forward = direction.normalized;

        // Detach trail from owner hierarchy so it renders correctly
        if (trailRenderer != null)
            trailRenderer.Clear();
    }

    // ─────────────────────────────────────────────────────────
    // UPDATE — move bullet + continuous raycast (prevents tunnelling)
    // ─────────────────────────────────────────────────────────
    void Update()
    {
        if (hasHit) return;

        if (distanceTraveled >= maxRange)
        {
            Destroy(gameObject);
            return;
        }

        // Step size for this frame
        float stepSize = velocity.magnitude * Time.deltaTime;

        // ── Continuous collision raycast (handles fast bullets) ──
        if (Physics.SphereCast(
            transform.position,
            0.05f,
            velocity.normalized,
            out RaycastHit hit,
            stepSize + 0.05f,
            hitLayers,
            QueryTriggerInteraction.Ignore))
        {
            // Skip owner colliders
            if (!IsOwner(hit.collider))
            {
                ProcessHit(hit);
                return;
            }
        }

        // ── Apply gravity + move ──
        velocity.y -= Physics.gravity.y * -gravityScale * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;

        if (velocity.sqrMagnitude > 0.01f)
            transform.forward = velocity.normalized;

        distanceTraveled += stepSize;
    }

    // ─────────────────────────────────────────────────────────
    // FALLBACK — trigger collider (for slow bullets / objects)
    // ─────────────────────────────────────────────────────────
    void OnTriggerEnter(Collider other)
    {
        if (hasHit) return;
        if (IsOwner(other)) return;
        if (other.GetComponent<Bullet>() != null) return; // ignore other bullets

        ProcessHit(other.transform.position, other.gameObject);
    }

    // ─────────────────────────────────────────────────────────
    // CORE HIT LOGIC
    // ─────────────────────────────────────────────────────────
    void ProcessHit(RaycastHit hit)
    {
        hasHit = true;
        float damage = ComputeDamage();
        DeliverDamage(hit.collider, damage);
        SpawnImpactFX(hit.point, hit.normal);
        Destroy(gameObject);
    }

    void ProcessHit(Vector3 point, GameObject hitGO)
    {
        hasHit = true;
        float damage = ComputeDamage();

        // Try to get collider for damage delivery
        Collider col = hitGO.GetComponent<Collider>();
        if (col != null) DeliverDamage(col, damage);

        SpawnImpactFX(point, Vector3.up);
        Destroy(gameObject);
    }

    // ── Damage interpolation across thresholds ──
    float ComputeDamage()
    {
        if (distanceTraveled <= fullDamageRange) return baseDamage;
        if (distanceTraveled >= noDamageRange)   return minDamage;

        float t = (distanceTraveled - fullDamageRange) / (noDamageRange - fullDamageRange);
        return Mathf.Lerp(baseDamage, minDamage, t);
    }

    // ── Route damage to correct recipient ──
    void DeliverDamage(Collider col, float damage)
    {
        if (isEnemyBullet)
        {
            // Enemy bullet → hit PlayerHealth
            PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();
            if (ph != null) ph.TakeDamage(damage);
        }
        else
        {
            // Player bullet → hit IDamageable (enemies, destructibles, etc.)
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target != null) target.TakeDamage(damage);
        }
    }

    void SpawnImpactFX(Vector3 point, Vector3 normal)
    {
        if (impactEffectPrefab != null)
            Instantiate(impactEffectPrefab, point, Quaternion.LookRotation(normal));
    }

    bool IsOwner(Collider col) =>
        ownerObject != null &&
        (col.gameObject == ownerObject || col.transform.IsChildOf(ownerObject.transform));

#if UNITY_EDITOR
    // Visualise bullet path in scene view during play
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = isEnemyBullet ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.06f);
    }
#endif
}