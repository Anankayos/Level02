using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    [Header("Visual (optional)")]
    [Tooltip("Child GameObject representing the 'active' state (flag, ring, etc.). " +
             "Disabled after the checkpoint is collected.")]
    public GameObject checkpointVisual;

    [Tooltip("Spawned at this position when the checkpoint is activated.")]
    public GameObject activationFXPrefab;

    [Header("Settings")]
    [Tooltip("If true, player must be grounded to activate the checkpoint. " +
             "Prevents mid-air accidental triggers.")]
    public bool requireGrounded = false;

    [Tooltip("Optional explicit spawn point. If null, uses this collider's geometric center projected to the floor.")]
    [SerializeField] private Transform spawnPoint;

    // Runtime
    private bool _activated;
    private Collider _col;

    void Awake()
    {
        _col = GetComponent<Collider>();

        // Show visual on start (checkpoint is fresh / available)
        if (checkpointVisual != null)
            checkpointVisual.SetActive(true);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_activated) return;
        if (!other.CompareTag("Player")) return;

        if (requireGrounded)
        {
            var cc = other.GetComponent<CharacterController>();
            if (cc != null && !cc.isGrounded) return;
        }

        Activate(other.gameObject);
    }

    void Activate(GameObject player)
    {
        _activated = true;

        Vector3 respawnPos = GetRespawnPosition();

        // ── 1. Tell CheckpointManager to save state + store respawn pos ──
        CheckpointManager cm = CheckpointManager.Instance;
        if (cm != null)
            cm.SaveCheckpoint(respawnPos, transform.rotation);
        else
            Debug.LogWarning("[Checkpoint] CheckpointManager.Instance is null. " +
                             "Make sure CheckpointManager exists in the scene.");

        // ── 2. Deactivate this checkpoint so it can't be taken again ──────
        if (_col != null) _col.enabled = false;

        if (checkpointVisual != null)
            checkpointVisual.SetActive(false);

        // ── 3. Spawn activation FX ────────────────────────────────────────
        if (activationFXPrefab != null)
            Instantiate(activationFXPrefab, respawnPos, Quaternion.identity);

        Debug.Log($"[Checkpoint] '{name}' activated. Respawn set to {respawnPos}");
    }

    Vector3 GetRespawnPosition()
    {
        if (spawnPoint != null)
            return spawnPoint.position;

        if (_col != null)
        {
            Bounds b = _col.bounds;
            return new Vector3(b.center.x, b.min.y, b.center.z);
        }

        return transform.position;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = _activated
            ? new Color(0.5f, 0.5f, 0.5f, 0.3f)   // grey = used
            : new Color(0f,   1f,   0.4f, 0.4f);   // green = available

        Collider col = GetComponent<Collider>();
        if (col is BoxCollider box)
        {
            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = old;
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, 1f);
        }
    }
#endif
}
