using UnityEngine;

public class TrainDamageZone : MonoBehaviour
{
    [Header("Damage")]
    [Tooltip("Instant damage dealt when the train first hits the player.")]
    public int impactDamage = 50;

    [Tooltip("Damage per second while the player is crushed inside the zone.")]
    public int crushDamagePerSecond = 20;

    [Header("Zone Identity (for debug)")]
    public string zoneName = "Front";   // Front / Left / Right

    private float _crushTimer;

    // ─────────────────────────────────────────────────────────
    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null) return;

        Debug.Log($"[Train/{zoneName}] Hit player — impact {impactDamage} dmg");
        health.TakeDamage(impactDamage);
        _crushTimer = 0f;
    }

    void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null) return;

        // Tick continuous crush damage
        _crushTimer += Time.deltaTime;
        if (_crushTimer >= 1f)
        {
            _crushTimer -= 1f;
            health.TakeDamage(crushDamagePerSecond);
            Debug.Log($"[Train/{zoneName}] Crush tick — {crushDamagePerSecond} dmg");
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
            _crushTimer = 0f;
    }

#if UNITY_EDITOR
    // Draw the trigger zone in Scene view so you can see coverage
    void OnDrawGizmos()
    {
        var col = GetComponent<BoxCollider>();
        if (col == null) return;

        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(col.center, col.size);
        Gizmos.matrix = oldMatrix;
    }
#endif
}