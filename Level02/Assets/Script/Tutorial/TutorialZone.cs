using UnityEngine;

public class TutorialZone : MonoBehaviour
{
    [Header("Tutorial")]
    [SerializeField] private TutorialData tutorialData;
    [SerializeField] private bool triggerOnce = true;
    [SerializeField] private string playerTag = "Player";

    [Header("Detection")]
    [SerializeField] private float radius = 3f;
    [SerializeField] private bool useXZOnly = true;

    [Header("Floor Separation")]
    [Tooltip("Max Y difference between player and zone to allow trigger. Tune to ~half your floor height.")]
    [SerializeField] private float maxHeightDifference = 2f;

    // ── Runtime ───────────────────────────────────────────────
    private Transform player;
    private bool      hasTriggered;

    // ═════════════════════════════════════════════════════════
    // INIT
    // ═════════════════════════════════════════════════════════

    void Start()
    {
        AutoFindPlayer();
    }

    // ═════════════════════════════════════════════════════════
    // UPDATE
    // ═════════════════════════════════════════════════════════

    void Update()
    {
        if (player == null)   { AutoFindPlayer(); return; }
        if (hasTriggered && triggerOnce) return;

        float xzDist = FlatDistance(transform.position, player.position);
        float yDist  = Mathf.Abs(player.position.y - transform.position.y);

        if (xzDist <= radius && yDist <= maxHeightDifference)
        {
            Trigger();
        }
    }

    // ═════════════════════════════════════════════════════════
    // TRIGGER
    // ═════════════════════════════════════════════════════════

    private void Trigger()
    {
        if (tutorialData == null)
        {
            Debug.LogWarning("[TutorialZone] No TutorialData assigned on " + gameObject.name);
            return;
        }

        TutorialManager.Instance?.Show(tutorialData);

        if (triggerOnce) hasTriggered = true;
    }

    public void ResetZone()
    {
        hasTriggered = false;
    }

    // ═════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════

    private void AutoFindPlayer()
    {
        GameObject go = GameObject.FindWithTag(playerTag);
        if (go != null) player = go.transform;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ═════════════════════════════════════════════════════════
    // GIZMOS
    // ═════════════════════════════════════════════════════════

    private void OnDrawGizmosSelected()
    {
        // XZ radius ring — shows horizontal detection range
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, radius);

        // Vertical range — shows floor separation band
        Gizmos.color = Color.yellow;
        Vector3 top    = transform.position + Vector3.up    * maxHeightDifference;
        Vector3 bottom = transform.position + Vector3.down  * maxHeightDifference;
        Gizmos.DrawLine(top, bottom);
        Gizmos.DrawWireSphere(top,    0.15f);
        Gizmos.DrawWireSphere(bottom, 0.15f);
    }
}