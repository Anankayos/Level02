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

    [Header("Debug")]
    [Tooltip("Print detection status to Console every N seconds. 0 = every frame (spammy).")]
    public float debugLogInterval = 1.0f;

    // ── Runtime ───────────────────────────────────────────────
    private Animator     _animator;
    private PlayerHealth _health;
    private EnemyAI      _currentTarget;

    private int  _crouchHash;
    private bool _crouchParamExists;

    private float _debugTimer;

    // ─────────────────────────────────────────────────────────
    void Awake()
    {
        _animator = GetComponent<Animator>();
        _health   = GetComponent<PlayerHealth>();
    }

    void Start()
    {
        // ── Startup diagnostics ──────────────────────────────
        if (_animator == null)
            Debug.LogError("[StealthKill] No Animator found on this GameObject. "
                         + "Crouch check will always fail. Attach StealthKill to the "
                         + "same GameObject as the Animator, or enable forceCrouchAlwaysOn.");

        if (_health == null)
            Debug.LogWarning("[StealthKill] No PlayerHealth found on this GameObject. "
                           + "Dead-player guard is disabled.");

        if (promptUI == null)
            Debug.LogWarning("[StealthKill] promptUI is not assigned in the Inspector. "
                           + "The kill prompt will never appear.");

        if (enemyLayer.value == 0)
            Debug.LogError("[StealthKill] enemyLayer mask is empty (value = 0). "
                         + "OverlapSphere will never hit any enemy. "
                         + "Assign the correct layer in the Inspector.");

        // ── Cache crouch parameter ───────────────────────────
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
                Debug.LogError($"[StealthKill] Animator parameter '{crouchAnimParam}' (Bool) not found. "
                             + $"Parameters available: {ListAnimParams()} "
                             + "Either fix the parameter name or enable forceCrouchAlwaysOn.");
            else
                Debug.Log($"[StealthKill] Crouch param '{crouchAnimParam}' cached OK (hash={_crouchHash}).");
        }

        Debug.Log($"[StealthKill] Initialized — killRange={killRange}, behindAngle={behindAngle}, "
                + $"enemyLayer={enemyLayer.value}, forceCrouchAlwaysOn={forceCrouchAlwaysOn}");
    }

    void OnDisable()
    {
        HidePrompt();
        _currentTarget = null;
    }

    // ─────────────────────────────────────────────────────────
    void Update()
    {
        if (_health != null && _health.IsDead) { HidePrompt(); return; }

        bool shouldLog = false;
        _debugTimer -= Time.deltaTime;
        if (_debugTimer <= 0f)
        {
            _debugTimer = debugLogInterval > 0f ? debugLogInterval : 0f;
            shouldLog   = true;
        }

        _currentTarget = FindKillTarget(shouldLog);

        if (_currentTarget != null)
        {
            ShowPrompt();

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
    EnemyAI FindKillTarget(bool log)
    {
        bool crouching = IsCrouching();

        if (log)
            Debug.Log($"[StealthKill] IsCrouching={crouching} "
                    + $"(forceCrouchAlwaysOn={forceCrouchAlwaysOn}, "
                    + $"animatorNull={_animator == null}, "
                    + $"paramExists={_crouchParamExists}, "
                    + $"animValue={(_crouchParamExists && _animator != null ? _animator.GetBool(_crouchHash).ToString() : "N/A")})");

        if (!crouching)
        {
            if (log) Debug.Log("[StealthKill] BLOCKED — player is not crouching.");
            return null;
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, killRange, enemyLayer);

        if (log)
            Debug.Log($"[StealthKill] OverlapSphere at {transform.position} "
                    + $"radius={killRange} layer={enemyLayer.value} → {hits.Length} collider(s) hit.");

        if (hits.Length == 0 && log)
            Debug.LogWarning("[StealthKill] No colliders found. Check: "
                           + "(1) enemyLayer mask matches the enemy's layer, "
                           + "(2) killRange is large enough, "
                           + "(3) enemy has a Collider component.");

        EnemyAI best     = null;
        float   bestDist = float.MaxValue;

        foreach (var col in hits)
        {
            EnemyAI enemy = col.GetComponentInParent<EnemyAI>();

            if (enemy == null)
            {
                if (log) Debug.Log($"[StealthKill] Collider '{col.name}' skipped — no EnemyAI in parent chain.");
                continue;
            }

            if (!enemy.IsAlive)
            {
                if (log) Debug.Log($"[StealthKill] '{enemy.name}' skipped — not alive.");
                continue;
            }

            if (enemy.IsAlerted)
            {
                if (log) Debug.Log($"[StealthKill] '{enemy.name}' skipped — IsAlerted (Combat/Melee state).");
                continue;
            }

            Vector3 toPlayer    = (transform.position - enemy.transform.position).normalized;
            float   angleToBack = Vector3.Angle(enemy.transform.forward, toPlayer);
            float   required    = 180f - behindAngle;

            if (angleToBack < required)
            {
                if (log) Debug.Log($"[StealthKill] '{enemy.name}' skipped — angle check failed. "
                                 + $"angleToBack={angleToBack:F1}° (need >= {required:F1}°). "
                                 + "Player is not far enough behind the enemy.");
                continue;
            }

            float dist = Vector3.Distance(transform.position, enemy.transform.position);
            if (log) Debug.Log($"[StealthKill] '{enemy.name}' VALID — dist={dist:F2}, angleToBack={angleToBack:F1}°.");

            if (dist < bestDist)
            {
                bestDist = dist;
                best     = enemy;
            }
        }

        if (log && best == null)
            Debug.Log("[StealthKill] No valid target found this tick.");
        else if (log && best != null)
            Debug.Log($"[StealthKill] Best target selected: '{best.name}' at dist={bestDist:F2}.");

        return best;
    }

    // ─────────────────────────────────────────────────────────
    void PerformKill(EnemyAI enemy)
    {
        if (enemy == null) return;

        Debug.Log($"[StealthKill] Executing kill on '{enemy.name}'.");

        Vector3 dir = enemy.transform.position - transform.position;
        dir.y = 0f;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(dir);

        _animator?.SetTrigger("StealthKill");
        enemy.ExecuteStealthKill();
        HidePrompt();
    }

    // ─────────────────────────────────────────────────────────
    bool IsCrouching()
    {
        if (forceCrouchAlwaysOn)  return true;
        if (_animator == null)    return false;
        if (!_crouchParamExists)  return false;
        return _animator.GetBool(_crouchHash);
    }

    void ShowPrompt()
    {
        if (promptUI == null)
        {
            Debug.LogWarning("[StealthKill] ShowPrompt called but promptUI is null. Assign it in the Inspector.");
            return;
        }
        promptUI.SetActive(true);
    }

    void HidePrompt() { if (promptUI != null) promptUI.SetActive(false); }

    // ─────────────────────────────────────────────────────────
    string ListAnimParams()
    {
        if (_animator == null) return "(no animator)";
        var names = new System.Text.StringBuilder();
        foreach (var p in _animator.parameters)
            names.Append($"{p.name}({p.type}) ");
        return names.Length > 0 ? names.ToString() : "(none)";
    }

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
