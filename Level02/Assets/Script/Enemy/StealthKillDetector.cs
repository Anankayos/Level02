using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Detects valid stealth kill targets near the player and shows/hides the prompt.
///
/// FIX HISTORY:
/// - Added IsCrouching() gate: prompt only fires when player is crouched.
/// - Removed _promptVisible latch pattern: detection resets every frame so
///   standing up immediately hides the prompt regardless of enemy proximity.
/// - Added OnDisable cleanup to prevent stale prompt on component/GameObject disable.
/// </summary>
public class StealthKillDetector : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Max distance to attempt a stealth kill")]
    [SerializeField] private float detectionRange = 2.2f;

    [Tooltip("Max angle from behind the enemy (180 = any direction, 130 = mostly behind)")]
    [SerializeField] private float behindAngle = 130f;

    [Header("Crouch Check")]
    [Tooltip("Animator bool parameter name that represents crouching.")]
    [SerializeField] private string crouchAnimParam = "IsCrouching";

    [Tooltip("Skip crouch requirement (useful while crouch is not yet implemented).")]
    [SerializeField] private bool forceCrouchAlwaysOn = false;

    [Header("Layer")]
    [SerializeField] private LayerMask enemyLayer;

    // ── Runtime ───────────────────────────────────────────────
    private Animator _animator;
    private EnemyAI  _targetEnemy;

    // Cached animator parameter
    private int  _crouchHash;
    private bool _crouchParamExists;


    // ═════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═════════════════════════════════════════════════════════

    private void Awake()
    {
        _animator = GetComponentInParent<Animator>();
        if (_animator == null)
            _animator = GetComponent<Animator>();
    }

    private void Start()
    {
        // Cache crouch hash once — avoids string lookup every frame
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
                Debug.LogWarning($"[StealthKillDetector] Animator parameter '{crouchAnimParam}' not found. "
                               + "Enable 'forceCrouchAlwaysOn' to bypass the crouch requirement.");
        }
    }

    private void OnDisable()
    {
        // FIX: always clean up prompt and target when this component is disabled
        // prevents stale 'Press E' prompt staying on screen
        GameEvents.FireHideStealthPrompt();
        _targetEnemy = null;
    }


    // ═════════════════════════════════════════════════════════
    // UPDATE
    // ═════════════════════════════════════════════════════════

    private void Update()
    {
        EnemyAI best = FindBestTarget();

        // FIX: evaluate and fire events every frame based on current state,
        // NOT only when the target changes. This means standing up immediately
        // hides the prompt even if the same enemy is still in range.
        if (best != null)
        {
            if (_targetEnemy != best)
            {
                // New target acquired — fire show only on actual change to avoid
                // spamming the event bus every frame
                GameEvents.FireShowStealthPrompt();
            }
            _targetEnemy = best;
        }
        else
        {
            if (_targetEnemy != null)
                GameEvents.FireHideStealthPrompt();

            // FIX: always null the target when no valid target found this frame
            _targetEnemy = null;
        }

        // Execute on E press
        if (_targetEnemy != null &&
            Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame)
        {
            _targetEnemy.ExecuteStealthKill();
            GameEvents.FireHideStealthPrompt();
            _targetEnemy = null;
        }
    }


    // ═════════════════════════════════════════════════════════
    // DETECTION
    // ═════════════════════════════════════════════════════════

    private EnemyAI FindBestTarget()
    {
        // FIX: crouch is now a hard gate — no crouch, no valid target, no prompt
        if (!IsCrouching()) return null;

        Collider[] hits = Physics.OverlapSphere(
            transform.position, detectionRange, enemyLayer);

        EnemyAI best     = null;
        float   bestDist = float.MaxValue;

        foreach (var col in hits)
        {
            EnemyAI enemy = col.GetComponentInParent<EnemyAI>();
            if (enemy == null || !enemy.IsAlive) continue;

            // FIX: skip enemies already in full combat/alert — they are aware of the player
            if (enemy.IsAlerted) continue;

            // Must be approaching from behind the enemy
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
    private bool IsCrouching()
    {
        if (forceCrouchAlwaysOn)  return true;
        if (_animator == null)    return false;
        if (!_crouchParamExists)  return false;
        return _animator.GetBool(_crouchHash);
    }
}
