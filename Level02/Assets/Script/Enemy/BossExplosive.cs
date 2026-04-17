using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BossExplosive : MonoBehaviour
{
    [Header("Explosion")]
    [SerializeField] private float fuseTime      = 3f;
    [SerializeField] private float blastRadius   = 4f;
    [SerializeField] private float blastDamage   = 35f;
    [SerializeField] private LayerMask damageMask;
    [SerializeField] private GameObject explosionVFX;

    private void Start() => StartCoroutine(FuseCoroutine());

    private IEnumerator FuseCoroutine()
    {
        yield return new WaitForSeconds(fuseTime);
        Explode();
    }

    private void Explode()
    {
        if (explosionVFX)
            Instantiate(explosionVFX, transform.position, Quaternion.identity);

        // Damage everything in radius that implements IDamageable
        Collider[] hits = Physics.OverlapSphere(transform.position, blastRadius, damageMask);
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent<IDamageable>(out var target))
                target.TakeDamage(blastDamage, gameObject);
        }

        Destroy(gameObject);
    }

    // Visualize blast radius in editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.35f);
        Gizmos.DrawSphere(transform.position, blastRadius);
    }
}