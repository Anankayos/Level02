using UnityEngine;

public class BossCoverSystem : MonoBehaviour
{
    [Header("Drag all 5 cover point Transforms here")]
    [SerializeField] private Transform[] coverPoints;

    private int _lastCoverIndex = -1;

    // Returns a cover position that is NOT the current one
    // and is occluded from the player
    public bool TryGetCoverPosition(Vector3 playerPos, out Vector3 result)
    {
        result = Vector3.zero;

        if (coverPoints == null || coverPoints.Length == 0)
        {
            Debug.LogWarning("[BossCoverSystem] No cover points assigned!");
            return false;
        }

        // Build shuffled list excluding last used cover
        int best      = -1;
        float bestDot = float.MaxValue;

        for (int i = 0; i < coverPoints.Length; i++)
        {
            if (i == _lastCoverIndex) continue;
            if (coverPoints[i] == null) continue;

            // Prefer cover points that are most "behind" something from player's view
            Vector3 toPoint = (coverPoints[i].position - playerPos).normalized;
            float   dot     = Vector3.Dot(toPoint, Vector3.up); // rough occlusion heuristic

            // Pick the point farthest from player but not the same as last
            float dist = Vector3.Distance(playerPos, coverPoints[i].position);
            if (dist > bestDot)
            {
                bestDot = dist;
                best    = i;
            }
        }

        if (best < 0) best = 0; // fallback to first point

        _lastCoverIndex = best;
        result          = coverPoints[best].position;
        return true;
    }

    public void MoveToCover(Vector3 pos)
    {
        var agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            agent.SetDestination(pos);
    }

    public void Reset() => _lastCoverIndex = -1;
}