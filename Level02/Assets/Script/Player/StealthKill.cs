using UnityEngine;
using UnityEngine.InputSystem;

public class StealthKill : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Max distance to perform a stealth kill.")]
    public float killRange = 2.0f;

    [Tooltip(
        "Max angle from directly behind the enemy (0 = exactly behind, 90 = side, 180 = any direction).\n" +
        "130 is a good default: forgiving rear arc without allowing frontal kills.")]
    public float behindAngle = 130f;

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
    private Animator     _animator;
    private PlayerHealth _health;
    private EnemyAI      _currentTarget;

    // Cached hash for crouch param — set in Start(), never loops at runtime
    private int  _crouchHash;
    private bool _crouchParamExists;

    // ─────────────────────────────────────────────────────────
    void Awake()
    {
        _animator = GetComponent<Animator>();
        _health   = GetComponent<PlayerHealth>();
    }

    void Start()
    {
        // Cache crouch parameter once — O(1) lookup every frame afterwards
        _crouchParamExists = false;
        if (_animator != null)
        {
            foreach (var p in _animator.parameters)
            {
                if (p.name == crouchAnimParam && p.type == AnimatorControllerParameterType.Bool)
                {
                    _crouchHash        = Animator.StringToHash(crouchAnimParam);
                    _crouchParamExists = true;
                    break;
                }
            }

            if (!_crouchParamExists)
                Debug.LogWarning($"[StealthKill] Animator parameter '{crouchAnimParam}' not found. "
                               + "Set 'forceCrouchAlwaysOn = true' to skip the crouch check.");
        }
    }

    // FIX: clean up prompt and target when component or GameObject is disabled
    void OnDisable()
    {
        HidePrompt();
        _currentTarget = null;
    }

    // ─────────────────────────────────────────────────────────
    void Update()
    {
        if (_health != null && _health.IsDead) { HidePrompt(); return; }

        _currentTarget = FindKillTarget();

        if (_currentTarget != null)
        {
            ShowPrompt();

            // E key or Gamepad South (cross / A)
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

        Collider[] hits = Physics.OverlapSphere(transform.position, killRange, enemyLayer);

        EnemyAI best     = null;
        float   bestDist = float.MaxValue;

        foreach (var col in hits)
        {
            EnemyAI enemy = col.GetComponentInParent<EnemyAI>();

            if (enemy == null || !enemy.IsAlive) continue;

            // FIX: enemies in Combat or Melee state are fully alerted and facing the player
            // — they cannot be stealth-killed. Skip them.
            if (enemy.IsAlerted) continue;

            // ── Behind-enemy angle check ──────────────────────────────────
            // angleToBack == 0   → player is directly behind the enemy
            // angleToBack == 180 → player is directly in front of the enemy
            // We allow the kill when angleToBack <= behindAngle.
            Vector3 toPlayer    = (transform.position - enemy.transform.position).normalized;
            float   angleToBack = Vector3.Angle(enemy.transform.forward, toPlayer);
            if (angleToBack < (180f - behindAngle)) continue;

            float dist = Vector3.Distance(transform.position, enemy.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best     = enemy;
            }
        }

        return best;
    }

    // ─────────────────────────────────────────────────────────
    void PerformKill(EnemyAI enemy)
    {
        if (enemy == null) return;

        // Face the enemy
        Vector3 dir = enemy.transform.position - transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(dir);

        _animator?.SetTrigger("StealthKill");
        enemy.ExecuteStealthKill();
        HidePrompt();
        Debug.Log($"[StealthKill] Executed on {enemy.name}");
    }

    // ─────────────────────────────────────────────────────────
    bool IsCrouching()
    {
        if (forceCrouchAlwaysOn)    return true;
        if (_animator == null)      return false;
        if (!_crouchParamExists)    return false;

        return _animator.GetBool(_crouchHash);
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
