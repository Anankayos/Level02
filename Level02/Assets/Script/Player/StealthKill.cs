using UnityEngine;
using UnityEngine.InputSystem;

public class StealthKill : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Max distance to perform a stealth kill.")]
    public float killRange      = 2.0f;

    [Tooltip("Max angle (degrees) between player forward and enemy direction. " +
             "180 = any direction (front AND back kill). 90 = front half only.")]
    public float angleThreshold = 180f;

    [Header("Crouch Check")]
    [Tooltip("Animator bool parameter name that represents crouching.")]
    public string crouchAnimParam = "IsCrouching";

    [Tooltip("Skip crouch requirement (useful while crouch is not yet implemented).")]
    public bool forceCrouchAlwaysOn = false;

    [Header("UI")]
    [Tooltip("'Press E to stealth kill' prompt — shown when kill is available.")]
    public GameObject promptUI;

    [Header("Layer")]
    [Tooltip("Layer(s) the enemy is on — used for sphere overlap check.")]
    public LayerMask enemyLayer;

    // ── Runtime ───────────────────────────────────────────────
    private Animator    _animator;
    private PlayerHealth _health;
    private EnemyAI     _currentTarget;

    void Awake()
    {
        _animator = GetComponent<Animator>();
        _health   = GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (_health != null && _health.IsDead) { HidePrompt(); return; }

        _currentTarget = FindKillTarget();

        if (_currentTarget != null)
        {
            ShowPrompt();

            // E key or Gamepad South (cross/A)
            bool pressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
            if (!pressed && Gamepad.current != null)
                pressed = Gamepad.current.buttonSouth.wasPressedThisFrame;

            if (pressed)
                PerformKill(_currentTarget);
        }
        else
        {
            HidePrompt();
        }
    }

    // ─────────────────────────────────────────────────────────
    EnemyAI FindKillTarget()
    {
        if (!IsCrouching()) return null;

        // Sphere overlap to find nearby enemies
        Collider[] hits = Physics.OverlapSphere(transform.position, killRange, enemyLayer);

        EnemyAI best      = null;
        float   bestDist  = float.MaxValue;

        foreach (var col in hits)
        {
            EnemyAI enemy = col.GetComponentInParent<EnemyAI>();
            if (enemy == null || !enemy.IsAlive || enemy.IsAlerted) continue;

            // Angle check
            Vector3 toEnemy = (enemy.transform.position - transform.position).normalized;
            float   angle   = Vector3.Angle(transform.forward, toEnemy);
            if (angle > angleThreshold * 0.5f) continue;

            float dist = Vector3.Distance(transform.position, enemy.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = enemy;
            }
        }
        return best;
    }

    void PerformKill(EnemyAI enemy)
    {
        if (enemy == null) return;

        // Face the enemy smoothly
        Vector3 dir = (enemy.transform.position - transform.position);
        dir.y = 0f;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(dir);

        // Trigger player animation (optional)
        _animator?.SetTrigger("StealthKill");

        enemy.ExecuteStealthKill();
        HidePrompt();
        Debug.Log($"[StealthKill] Executed on {enemy.name}");
    }

    // ─────────────────────────────────────────────────────────
    bool IsCrouching()
    {
        if (forceCrouchAlwaysOn) return true;
        if (_animator == null) return false;

        // Try to read the Crouch bool from the animator
        foreach (var p in _animator.parameters)
        {
            if (p.name == crouchAnimParam && p.type == AnimatorControllerParameterType.Bool)
                return _animator.GetBool(crouchAnimParam);
        }

        // Parameter not found — log once, then return false
        Debug.LogWarning($"[StealthKill] Animator parameter '{crouchAnimParam}' not found. " +
                         "Set 'forceCrouchAlwaysOn = true' to skip crouch check.");
        return false;
    }

    void ShowPrompt() { if (promptUI != null) promptUI.SetActive(true); }
    void HidePrompt() { if (promptUI != null) promptUI.SetActive(false); }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0.4f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, killRange);

        if (_currentTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(transform.position, _currentTarget.transform.position);
        }
    }
#endif
}