using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to the boss bomb prefab.
/// BossAI.ThrowCoroutine calls Launch(targetPos) immediately after Instantiate.
///
/// Flow:
///   1. Launch() calculates the arc velocity to land on targetPos
///   2. Rigidbody flies through the air
///   3. After fuseTime (3s) Explode() fires regardless of collision
///   4. OnCollisionEnter triggers early explode if it hits something solid
///   5. Explode() does OverlapSphere area damage + optional VFX
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BossExplosive : MonoBehaviour
{
    [Header("Fuse")]
    [Tooltip("Seconds before the bomb explodes after being thrown.")]
    public float fuseTime = 3f;

    [Header("Damage")]
    public float damage         = 40f;
    public float explosionRadius = 4f;
    [Tooltip("Layers that receive damage (Player, Enemy, etc.).")]
    public LayerMask damageLayer;

    [Header("Arc")]
    [Tooltip("Extra upward height added to the throw arc. Higher = more lob.")]
    public float arcHeight = 4f;

    [Header("VFX")]
    [Tooltip("Particle/explosion prefab spawned on detonation. Optional.")]
    public GameObject explosionVFX;

    // ── Runtime ───────────────────────────────────────────────
    private Rigidbody _rb;
    private bool      _exploded  = false;
    private bool      _launched  = false;

    // ─────────────────────────────────────────────────────────
    private void Awake()
    {
        _rb          = GetComponent<Rigidbody>();
        _rb.useGravity = true;
    }

    // ─────────────────────────────────────────────────────────
    /// <summary>
    /// Called by BossAI immediately after Instantiate.
    /// Calculates a ballistic arc that lands on targetPos.
    /// </summary>
    public void Launch(Vector3 targetPos)
    {
        if (_launched) return;
        _launched = true;

        Vector3 origin    = transform.position;
        Vector3 launchVel = CalculateArcVelocity(origin, targetPos, arcHeight);
        _rb.linearVelocity = launchVel;

        Debug.Log($"[BossExplosive] Launched toward {targetPos} | vel={launchVel}");

        StartCoroutine(FuseCoroutine());
    }

    // ─────────────────────────────────────────────────────────
    private IEnumerator FuseCoroutine()
    {
        yield return new WaitForSeconds(fuseTime);
        Explode();
    }

    // ─────────────────────────────────────────────────────────
    /// <summary>
    /// Early detonation if the bomb hits a wall or floor before fuse runs out.
    /// </summary>
    private void OnCollisionEnter(Collision col)
    {
        // Ignore the boss itself
        if (col.gameObject.CompareTag("Boss")) return;
        Explode();
    }

    // ─────────────────────────────────────────────────────────
    private void Explode()
    {
        if (_exploded) return;
        _exploded = true;

        Debug.Log($"[BossExplosive] EXPLODED at {transform.position} radius={explosionRadius}");

        // ── Area damage ───────────────────────────────────────
        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius, damageLayer);
        foreach (var col in hits)
        {
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || !target.IsAlive) continue;

            // Scale damage by distance: full damage at centre, 50% at edge
            float dist   = Vector3.Distance(transform.position, col.transform.position);
            float factor = Mathf.Lerp(1f, 0.5f, dist / explosionRadius);
            float dealt  = damage * factor;

            target.TakeDamage(dealt, gameObject);
            Debug.Log($"[BossExplosive] Hit '{col.name}' for {dealt:F1} dmg (dist={dist:F2})");
        }

        // ── VFX ───────────────────────────────────────────────
        if (explosionVFX != null)
            Instantiate(explosionVFX, transform.position, Quaternion.identity);

        Destroy(gameObject);
    }

    // ─────────────────────────────────────────────────────────
    /// <summary>
    /// Calculates the launch velocity for a ballistic arc.
    /// Uses the standard projectile formula with a forced apex.
    ///
    /// h = arcHeight above the higher of origin/target.
    /// Gravity is read from Physics.gravity.y so it works with any gravity setting.
    /// </summary>
    private static Vector3 CalculateArcVelocity(Vector3 origin, Vector3 target, float arcHeight)
    {
        float g     = Mathf.Abs(Physics.gravity.y);  // positive magnitude
        float apex  = Mathf.Max(origin.y, target.y) + arcHeight;

        // Time to rise from origin to apex
        float h0    = apex - origin.y;
        float tUp   = Mathf.Sqrt(2f * h0 / g);

        // Time to fall from apex to target
        float h1    = apex - target.y;
        float tDown = Mathf.Sqrt(2f * h1 / g);

        float tTotal = tUp + tDown;
        if (tTotal <= 0f) tTotal = 0.1f;  // safety guard

        // Horizontal component (XZ)
        Vector3 horizontal = (target - origin);
        horizontal.y = 0f;
        Vector3 vel = horizontal / tTotal;

        // Vertical component
        vel.y = g * tUp;

        return vel;
    }

    // ─────────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
#endif
}
