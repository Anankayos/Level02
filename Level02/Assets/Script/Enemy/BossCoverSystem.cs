using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class BossCoverSystem : MonoBehaviour
{
    [Tooltip("Tag all pillar GameObjects with 'CoverPoint' and add a child Transform named 'CoverSpot'")]
    [SerializeField] private float coverSearchRadius = 25f;
    [SerializeField] private float minCoverDistance  = 3f;   // don't pick cover too close to player
    [SerializeField] private LayerMask obstacleMask;

    private NavMeshAgent _agent;

    private void Awake() => _agent = GetComponent<NavMeshAgent>();

    /// <summary>Returns the best cover position hiding from the target, or Vector3.zero if none found.</summary>
    public bool TryGetCoverPosition(Vector3 threatPos, out Vector3 coverPos)
    {
        coverPos = Vector3.zero;
        var candidates = new List<Transform>();

        // Find all cover-tagged objects in radius
        Collider[] cols = Physics.OverlapSphere(transform.position, coverSearchRadius);
        foreach (var col in cols)
        {
            if (!col.CompareTag("CoverPoint")) continue;
            Transform spot = col.transform.Find("CoverSpot") ?? col.transform;
            candidates.Add(spot);
        }

        float bestScore = float.MaxValue;
        foreach (var spot in candidates)
        {
            float distToThreat = Vector3.Distance(spot.position, threatPos);
            if (distToThreat < minCoverDistance) continue;

            // Prefer spots where the pillar blocks line-of-sight to the player
            bool lineBlocked = Physics.Linecast(spot.position + Vector3.up, threatPos + Vector3.up, obstacleMask);
            float score = lineBlocked
                        ? Vector3.Distance(transform.position, spot.position)       // near + blocked → best
                        : Vector3.Distance(transform.position, spot.position) + 50f; // penalise exposed

            if (score < bestScore)
            {
                bestScore = score;
                coverPos  = spot.position;
            }
        }

        return bestScore < float.MaxValue;
    }

    public void MoveToCover(Vector3 coverPos) => _agent.SetDestination(coverPos);
}