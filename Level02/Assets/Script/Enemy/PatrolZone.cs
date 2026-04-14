using UnityEngine;
using UnityEngine.AI;

public class PatrolZone : MonoBehaviour
{
    [Tooltip("How far from the zone edge to search for a valid NavMesh point")]
    [SerializeField] private float navSampleRadius = 3f;

    private Collider _bounds;

    private void Awake()
    {
        _bounds           = GetComponent<Collider>();
        _bounds.isTrigger = true; // never blocks player movement
    }

    // True if the world position is inside this zone
    public bool Contains(Vector3 worldPos)
    {
        // ClosestPoint returns the input unchanged if it's already inside
        return _bounds.ClosestPoint(worldPos) == worldPos;
    }

    // Returns the nearest NavMesh point that is still inside the zone.
    // Use this instead of SetDestination() directly — it will never send
    // the agent outside the boundary.
    public bool TryClampDestination(Vector3 desired, out Vector3 clamped)
    {
        // If the destination is already inside, use it as-is
        Vector3 target = Contains(desired) ? desired : _bounds.ClosestPoint(desired);

        NavMeshHit hit;
        if (NavMesh.SamplePosition(target, out hit, navSampleRadius, NavMesh.AllAreas))
        {
            clamped = hit.position;
            return true;
        }

        clamped = transform.position; // fallback to zone centre
        return false;
    }

    private void OnDrawGizmos()
    {
        if (!TryGetComponent<Collider>(out var col)) return;
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.18f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.7f);
        Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
    }
}