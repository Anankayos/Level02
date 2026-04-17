using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(BossCoverSystem))]
public class BossAI : MonoBehaviour, IDamageable, IResettable
{
    // ─────────────────────── Inspector ───────────────────────
    [Header("Core")]
    [SerializeField] private string bossName           = "UNIT-7 OVERSEER";
    [SerializeField] private float  maxHealth          = 500f;
    [SerializeField] private float  coverHealthPct     = 0.70f;  // seek cover below this %

    [Header("Shooting")]
    [SerializeField] private Transform  muzzlePoint;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float      fireRate       = 0.8f;   // seconds between shots
    [SerializeField] private int        burstCount     = 3;      // shots per burst
    [SerializeField] private float      accuracy       = 0.75f;

    [Header("Explosives")]
    [SerializeField] private Transform  throwOrigin;
    [SerializeField] private GameObject explosivePrefab;
    [SerializeField] private float      throwForce     = 12f;
    [SerializeField] private float      throwCooldown  = 8f;

    [Header("Movement")]
    [SerializeField] private float  chaseSpeed         = 4f;
    [SerializeField] private float  coverSpeed         = 6f;
    [SerializeField] private float  engageDistance     = 18f;

    [Header("Parts — drag the 5 BossPart children here")]
    [SerializeField] private List<BossPart> parts      = new();

    [Header("UI")]
    [SerializeField] private BossHealthBarUI healthBarUI;

    // ─────────────────────── State ───────────────────────────
    private enum BossState { Idle, Engaging, TakingCover, Throwing, Dead }
    private BossState _state = BossState.Idle;

    private float         _currentHealth;
    private NavMeshAgent  _agent;
    private BossCoverSystem _cover;
    private Transform     _player;
    private float         _lastFireTime;
    private float         _lastThrowTime;
    private int           _partsAlive = 5;
    private Vector3       _spawnPos;
    private Quaternion    _spawnRot;

    // ─────────────────────── Unity Lifecycle ─────────────────
    private void Awake()
    {
        _agent  = GetComponent<NavMeshAgent>();
        _cover  = GetComponent<BossCoverSystem>();
        _player = GameObject.FindGameObjectWithTag("Player")?.transform;
        _spawnPos = transform.position;
        _spawnRot = transform.rotation;
    }

    private void OnEnable()
    {
        BossPart.OnPartDestroyed += HandlePartDestroyed;
    }

    private void OnDisable()
    {
        BossPart.OnPartDestroyed -= HandlePartDestroyed;
    }

    private void Start()
    {
        _currentHealth = maxHealth;
        healthBarUI?.Initialize(maxHealth, bossName);
    }

    private void Update()
    {
        if (_state == BossState.Dead || _player == null) return;

        float distToPlayer = Vector3.Distance(transform.position, _player.position);

        switch (_state)
        {
            case BossState.Idle:
                if (distToPlayer < engageDistance) EnterEngage();
                break;

            case BossState.Engaging:
                FaceTarget(_player.position);
                _agent.SetDestination(_player.position);
                TryFireBurst();
                TryThrowExplosive();
                CheckForCover();
                break;

            case BossState.TakingCover:
                // Keep firing while moving to cover
                TryFireBurst();
                if (!_agent.pathPending && _agent.remainingDistance < 0.6f)
                    EnterEngage();
                break;

            case BossState.Throwing:
                // Handled by coroutine
                break;
        }
    }

    // ─────────────────────── State Transitions ───────────────
    private void EnterEngage()
    {
        _state = BossState.Engaging;
        _agent.speed = chaseSpeed;
    }

    private void CheckForCover()
    {
        float hpPct = _currentHealth / maxHealth;
        if (hpPct > coverHealthPct) return;                    // healthy — stay aggressive
        if (Time.time - _lastFireTime < 2f) return;            // just fired — finish burst first

        if (_cover.TryGetCoverPosition(_player.position, out Vector3 coverPos))
        {
            _state = BossState.TakingCover;
            _agent.speed = coverSpeed;
            _cover.MoveToCover(coverPos);
        }
    }

    // ─────────────────────── Shooting ────────────────────────
    private void TryFireBurst()
    {
        if (Time.time - _lastFireTime < fireRate) return;
        // Don't fire if player is behind cover (boss has no LoS)
        if (!HasLineOfSight()) return;

        _lastFireTime = Time.time;
        StartCoroutine(FireBurstCoroutine());
    }

    private IEnumerator FireBurstCoroutine()
    {
        for (int i = 0; i < burstCount; i++)
        {
            SpawnBullet();
            yield return new WaitForSeconds(0.12f);
        }
    }

    private void SpawnBullet()
    {
        if (!bulletPrefab || !muzzlePoint) return;

        Vector3 dir = (_player.position + Vector3.up * 0.8f) - muzzlePoint.position;
        dir = ApplyInaccuracy(dir);

        var go = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.LookRotation(dir));
        if (go.TryGetComponent<Rigidbody>(out var rb))
            rb.linearVelocity = dir.normalized * 40f;
    }

    private Vector3 ApplyInaccuracy(Vector3 dir)
    {
        float spread = 1f - accuracy;
        dir += new Vector3(
            Random.Range(-spread, spread),
            Random.Range(-spread * 0.5f, spread * 0.5f),
            Random.Range(-spread, spread));
        return dir.normalized;
    }

    private bool HasLineOfSight()
    {
        if (!muzzlePoint) return true;
        Vector3 to = _player.position + Vector3.up * 0.8f;
        return !Physics.Linecast(muzzlePoint.position, to, LayerMask.GetMask("Default", "Environment"));
    }

    // ─────────────────────── Explosives ──────────────────────
    private void TryThrowExplosive()
    {
        if (Time.time - _lastThrowTime < throwCooldown) return;
        // Only throw if boss has ≤ 3 parts alive (escalates mid-fight)
        if (_partsAlive > 3) return;

        _lastThrowTime = Time.time;
        StartCoroutine(ThrowCoroutine());
    }

    private IEnumerator ThrowCoroutine()
    {
        _state = BossState.Throwing;
        _agent.isStopped = true;
        FaceTarget(_player.position);

        yield return new WaitForSeconds(0.6f); // wind-up

        if (explosivePrefab && throwOrigin)
        {
            var go = Instantiate(explosivePrefab, throwOrigin.position, Quaternion.identity);
            if (go.TryGetComponent<Rigidbody>(out var rb))
            {
                Vector3 toPlayer = (_player.position - throwOrigin.position).normalized;
                rb.AddForce((toPlayer + Vector3.up * 0.5f) * throwForce, ForceMode.Impulse);
            }
        }

        yield return new WaitForSeconds(0.4f);
        _agent.isStopped = false;
        EnterEngage();
    }

    // ─────────────────────── Parts ───────────────────────────
    private void HandlePartDestroyed(BossPart part)
    {
        // Only react to parts on THIS boss
        if (!parts.Contains(part)) return;

        _partsAlive--;
        healthBarUI?.UpdateHealth(_currentHealth, _partsAlive);

        Debug.Log($"[Boss] {_partsAlive} parts remaining. Escalating...");

        // Each part destroyed = boss gets more aggressive
        fireRate   = Mathf.Max(0.3f, fireRate   - 0.08f);
        throwCooldown = Mathf.Max(3f, throwCooldown - 1f);

        // Force a cover-to-regroup moment
        if (_cover.TryGetCoverPosition(_player.position, out Vector3 pos))
        {
            _state = BossState.TakingCover;
            _cover.MoveToCover(pos);
        }
    }

    // ─────────────────────── IDamageable ─────────────────────
    public bool IsAlive => _state != BossState.Dead;

public void TakeDamage(float amount, GameObject source = null)
{
    if (_state == BossState.Dead) return;

    float reduction = _partsAlive * 0.08f;
    _currentHealth -= amount * (1f - reduction);
    _currentHealth  = Mathf.Max(0f, _currentHealth);

    healthBarUI?.UpdateHealth(_currentHealth, _partsAlive);

    if (_currentHealth <= 0f) Die();
}

    private void Die()
    {
        _state = BossState.Dead;
        _agent.isStopped = true;
        healthBarUI?.Hide();
        // Trigger death animation, drop loot, etc.
        Debug.Log("[Boss] DEFEATED.");
    }

    // ─────────────────────── IResettable ─────────────────────
   // ─────────────────────── IResettable ─────────────────────
public string ResettableID => gameObject.name;

public void SaveInitialState()
{
    // _spawnPos and _spawnRot are already captured in Awake()
    // nothing else to snapshot for the boss
}

public void ResetState()
{
    _state         = BossState.Idle;
    _currentHealth = maxHealth;
    _partsAlive    = 5;
    fireRate       = 0.8f;
    throwCooldown  = 8f;

    transform.SetPositionAndRotation(_spawnPos, _spawnRot);
    _agent.isStopped = false;
    _agent.Warp(_spawnPos);

    foreach (var part in parts) part.ResetPart();
    healthBarUI?.Initialize(maxHealth, bossName);
}
    // ─────────────────────── Helpers ─────────────────────────
    private void FaceTarget(Vector3 target)
    {
        Vector3 dir = (target - transform.position).normalized;
        dir.y = 0f;
        if (dir != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation,
                                                   Quaternion.LookRotation(dir), 8f * Time.deltaTime);
    }
}