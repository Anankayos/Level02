using UnityEngine;

[DefaultExecutionOrder(100)]   // run AFTER ThirdPersonController (order 0)
public class TrainMover : MonoBehaviour
{
    [Header("Path")]
    public Transform pointA;
    public Transform pointB;

    [Header("Movement")]
    public float speed            = 4f;
    public float arrivalThreshold = 0.1f;
    public float pauseAtEnd       = 0.4f;

    // ── Runtime ───────────────────────────────────────────────
    private Transform           _currentTarget;
    private Transform           _nextTarget;
    private float               _pauseTimer;
    private CharacterController _riderCC;

    // Called by TrainRoofTrigger.cs (on the child GO)
    public void SetRider(CharacterController cc) => _riderCC = cc;

    void Start()
    {
        if (pointA == null || pointB == null)
        {
            Debug.LogError("[TrainMover] Assign both Point A and Point B.");
            enabled = false;
            return;
        }
        _currentTarget = pointB;
        _nextTarget    = pointA;
    }

    void Update()
    {
        if (_pauseTimer > 0f)
        {
            _pauseTimer -= Time.deltaTime;
            return;
        }

        // ── Move train ────────────────────────────────────────
        Vector3 prev     = transform.position;
        Vector3 dir      = (_currentTarget.position - transform.position).normalized;
        float   distance = Vector3.Distance(transform.position, _currentTarget.position);

        transform.position += dir * speed * Time.deltaTime;

        if (distance <= arrivalThreshold)
        {
            transform.position = _currentTarget.position;
            _pauseTimer        = pauseAtEnd;
            (_currentTarget, _nextTarget) = (_nextTarget, _currentTarget);
        }

        // ── Carry rider: direct transform nudge AFTER TPC ran ─
        // We do NOT use CC.Move() here — that would fight TPC's
        // own Move() call. Instead we teleport the transform by
        // the exact delta the train moved this frame.
        // CC will resolve any geometry overlap on its next Move().
        Vector3 delta = transform.position - prev;
        if (_riderCC != null && delta != Vector3.zero)
            _riderCC.transform.position += delta;
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (pointA == null || pointB == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pointA.position, pointB.position);
        Gizmos.DrawWireSphere(pointA.position, 0.25f);
        Gizmos.DrawWireSphere(pointB.position, 0.25f);
    }
#endif
}