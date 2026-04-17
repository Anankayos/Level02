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

    [Tooltip("Base damage value")]
    public float baseDamage = 15f;

    [Tooltip("Minimum damage at extreme range")]
    public float minDamage = 5f;

    // ── FX ────────────────────────────────────────────────────
    [Header("FX (optional)")]
    public GameObject  impactEffectPrefab;
    public TrailRenderer trailRenderer;

    // ── Layer Filtering ───────────────────────────────────────
    [Header("Layer Filtering")]
    public LayerMask hitLayers = ~0;

    // ── Runtime ───────────────────────────────────────────────
    private Vector3    velocity;
    private float      distanceTraveled;
    private bool       isEnemyBullet;
    private GameObject ownerObject;
    private bool       hasHit;


    // ═════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═════════════════════════════════════════════════════════

    public void Initialize(Vector3 direction, bool fromEnemy, GameObject owner)
    {
        velocity         = direction.normalized * speed;
        isEnemyBullet    = fromEnemy;
        ownerObject      = owner;
        distanceTraveled = 0f;
        hasHit           = false;

        transform.forward = direction.normalized;

        if (trailRenderer != null)
            trailRenderer.Clear();
    }


    // ═════════════════════════════════════════════════════════
    // UPDATE
    // ═════════════════════════════════════════════════════════

    void Update()
    {
        if (hasHit) return;

        if (distanceTraveled >= maxRange)
        {
            Destroy(gameObject);
            return;
        }

        float stepSize = velocity.magnitude * Time.deltaTime;

        // Continuous SphereCast prevents tunnelling on fast bullets
        if (Physics.SphereCast(
            transform.position,
            0.05f,
            velocity.normalized,
            out RaycastHit hit,
            stepSize + 0.05f,
            hitLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (!IsOwner(hit.collider))
            {
                ProcessHit(hit);
                return;
            }
        }

        velocity.y -= Physics.gravity.y * -gravityScale * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;

        if (velocity.sqrMagnitude > 0.01f)
            transform.forward = velocity.normalized;

        distanceTraveled += stepSize;
    }


    // ═════════════════════════════════════════════════════════
    // FALLBACK TRIGGER
    // ═════════════════════════════════════════════════════════

    void OnTriggerEnter(Collider other)
    {
        if (hasHit) return;
        if (IsOwner(other)) return;
        if (other.GetComponent<Bullet>() != null) return;

        ProcessHit(other.transform.position, other.gameObject);
    }


    // ═════════════════════════════════════════════════════════
    // CORE HIT LOGIC
    // ═════════════════════════════════════════════════════════

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

        Collider col = hitGO.GetComponent<Collider>();
        if (col != null) DeliverDamage(col, damage);

        SpawnImpactFX(point, Vector3.up);
        Destroy(gameObject);
    }


    // ═════════════════════════════════════════════════════════
    // DAMAGE — DestructibleSurface checked BEFORE IDamageable
    // because DestructibleSurface also implements IDamageable.
    // Most specific type must always be first.
    // ═════════════════════════════════════════════════════════

    void DeliverDamage(Collider col, float damage)
    {
        if (isEnemyBullet)
        {
            // Enemy bullet → only damages PlayerHealth
            PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();
            if (ph != null) ph.TakeDamage(damage);
            return;
        }

        // ── 1. DestructibleSurface (most specific — checked first) ──
        DestructibleSurface ds = col.GetComponentInParent<DestructibleSurface>();
        if (ds != null)
        {
            ds.TakeDamage(damage);
            GameEvents.FireHit(HitType.Destructible);
            return;
        }

        // ── 2. IDamageable (enemies and anything else) ─────────────
        IDamageable target = col.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            target.TakeDamage(damage);
            bool isKill = target is EnemyAI e && !e.IsAlive;
            GameEvents.FireHit(isKill ? HitType.Kill : HitType.Enemy);
        }
    }


    // ═════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════

    float ComputeDamage()
    {
        if (distanceTraveled <= fullDamageRange) return baseDamage;
        if (distanceTraveled >= noDamageRange)   return minDamage;

        float t = (distanceTraveled - fullDamageRange) / (noDamageRange - fullDamageRange);
        return Mathf.Lerp(baseDamage, minDamage, t);
    }

    void SpawnImpactFX(Vector3 point, Vector3 normal)
    {
        if (impactEffectPrefab != null)
            Instantiate(impactEffectPrefab, point, Quaternion.LookRotation(normal));
    }

    bool IsOwner(Collider col) =>
        ownerObject != null &&
        (col.gameObject == ownerObject ||
         col.transform.IsChildOf(ownerObject.transform));


#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = isEnemyBullet ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.06f);
    }
#endif
}