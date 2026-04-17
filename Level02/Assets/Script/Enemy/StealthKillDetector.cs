using UnityEngine;
using UnityEngine.InputSystem;

public class StealthKillDetector : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("Max distance to attempt a stealth kill")]
    [SerializeField] private float detectionRange = 2.2f;

    [Tooltip("Max angle from behind the enemy (180 = any direction, 130 = mostly behind)")]
    [SerializeField] private float behindAngle = 130f;

    [Header("Layer")]
    [SerializeField] private LayerMask enemyLayer;

    // Runtime
    private EnemyAI _targetEnemy;
    private bool    _promptVisible;


    // ═════════════════════════════════════════════════════════
    // UPDATE
    // ═════════════════════════════════════════════════════════

    private void Update()
    {
        EnemyAI best = FindBestTarget();

        // Target changed — update prompt
        if (best != _targetEnemy)
        {
            _targetEnemy = best;

            if (_targetEnemy != null)
            {
                GameEvents.FireShowStealthPrompt();
                _promptVisible = true;
            }
            else if (_promptVisible)
            {
                GameEvents.FireHideStealthPrompt();
                _promptVisible = false;
            }
        }

        // Execute on E press
        if (_targetEnemy != null &&
            Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame)
        {
            _targetEnemy.ExecuteStealthKill();
            GameEvents.FireHideStealthPrompt();
            _targetEnemy   = null;
            _promptVisible = false;
        }
    }

    private void OnDisable()
    {
        if (_promptVisible)
        {
            GameEvents.FireHideStealthPrompt();
            _promptVisible = false;
        }
        _targetEnemy = null;
    }


    // ═════════════════════════════════════════════════════════
    // DETECTION
    // ═════════════════════════════════════════════════════════

    private EnemyAI FindBestTarget()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position, detectionRange, enemyLayer);

        EnemyAI best     = null;
        float   bestDist = float.MaxValue;

        foreach (var col in hits)
        {
            EnemyAI enemy = col.GetComponentInParent<EnemyAI>();
            if (enemy == null || !enemy.IsAlive || enemy.IsAlerted) continue;

            // Must be approaching from behind the enemy
            Vector3 toPlayer    = (transform.position - enemy.transform.position).normalized;
            float   angleToBack = Vector3.Angle(enemy.transform.forward, toPlayer);
            if (angleToBack < (180f - behindAngle)) continue;

            // ── BUG FIX: always update bestDist when closer enemy found ──
            float dist = Vector3.Distance(transform.position, enemy.transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;   // was: bestDist = best == null ? dist : bestDist
                best     = enemy;
            }
        }

        return best;
    }
}