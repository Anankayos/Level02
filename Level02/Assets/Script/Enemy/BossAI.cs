using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class BossAI : MonoBehaviour, IDamageable, IResettable
{
    // ───────────────────────────────────────────────────────────────────────
    //  Inspector
    // ───────────────────────────────────────────────────────────────────────
    [Header("Core")]
    [SerializeField] private string bossName        = "UNIT-7 OVERSEER";
    [SerializeField] private float  maxHealth       = 500f;
    [SerializeField] private float  patrolHealthPct = 0.70f;

    [Header("Z-Axis Lock")]
    [SerializeField] private float lockedZ     = 0f;
    [SerializeField] private float strafeRange = 6f;

    [Header("Shooting")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float      fireRate   = 0.8f;
    [SerializeField] private int        burstCount = 3;
    [SerializeField] private float      accuracy   = 0.75f;

    [Header("Explosives")]
    [Tooltip("Assign the BossExplosive prefab here.")]
    [SerializeField] private GameObject explosivePrefab;
    [Tooltip("Empty child on the boss mesh used as bomb spawn point.")]
    [SerializeField] private Transform  throwOrigin;
    [SerializeField] private float      throwCooldown = 6f;
    [Tooltip("Boss starts throwing bombs after this many parts are destroyed.")]
    [SerializeField] private int        grenadesUnlockAtPartsDestroyed = 2;

    [Header("Movement")]
    [SerializeField] private float chaseSpeed     = 4f;
    [SerializeField] private float patrolSpeed    = 7f;
    [SerializeField] private float engageDistance = 20f;

    [Header("Patrol Points")]
    [Tooltip("Drag 5 empty GameObjects here for the boss to move between.")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float timeAtPoint = 3f;

    [Header("Parts")]
    [SerializeField] private List<BossPart> parts = new();

    [Header("UI")]
    [SerializeField] private BossHealthBarUI healthBarUI;

    [Header("Arena")]
    [SerializeField] private BossArenaWall arenaWall;
    [SerializeField] private BossBridge    arenaBridge;

    [Header("Animation")]
    [SerializeField] private Animator bossAnimator;
    public GameObject elevator;

    // ───────────────────────────────────────────────────────────────────────
    //  State
    // ───────────────────────────────────────────────────────────────────────
    private enum BossState { Idle, Engaging, Patrolling, Throwing, Dead }
    private BossState _state = BossState.Idle;

    public bool FightStarted { get; private set; } = false;

    private float        _currentHealth;
    private NavMeshAgent _agent;
    private Transform    _player;

    // FIX: two independent timers — burst shooting and bomb throwing
    // no longer share a timestamp, so one can never block the other.
    private float _lastFireTime;   // used ONLY by TryFireBurst
    private float _lastThrowTime;  // used ONLY by TryThrowExplosive

    private int          _partsDestroyed     = 0;
    private int          _partsAlive         = 5;
    private Vector3      _spawnPos;
    private Quaternion   _spawnRot;
    private bool         _battleMusicActive  = false;
    private int          _currentPatrolIndex = 0;

    private Coroutine _patrolCoroutine;
    private Coroutine _throwCoroutine;

    // ───────────────────────────────────────────────────────────────────────
    //  Lifecycle
    // ───────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _agent    = GetComponent<NavMeshAgent>();
        _player   = GameObject.FindGameObjectWithTag("Player")?.transform;
        _spawnPos = transform.position;
        _spawnRot = transform.rotation;
        lockedZ   = transform.position.z;

        BossPart.OnPartDestroyed += HandlePartDestroyed;
    }

    private void OnDestroy() => BossPart.OnPartDestroyed -= HandlePartDestroyed;

    private void Start() => _currentHealth = maxHealth;

    // ───────────────────────────────────────────────────────────────────────
    //  Update
    // ───────────────────────────────────────────────────────────────────────
    private void Update()
    {
        if (_player == null || _state == BossState.Dead) return;

        if (Time.frameCount % 60 == 0)
            Debug.Log($"[Boss] State={_state} | HP={_currentHealth:F0} | "
                    + $"PartsDestroyed={_partsDestroyed} | "
                    + $"BombsUnlocked={_partsDestroyed >= grenadesUnlockAtPartsDestroyed}");

        float dist = Vector3.Distance(transform.position, _player.position);

        switch (_state)
        {
            case BossState.Idle:
                if (dist < engageDistance) EnterEngage();
                break;

            case BossState.Engaging:
                FaceTarget(_player.position);
                EnforceZAxis();
                StrafeTowardPlayer();
                TryFireBurst();
                TryThrowExplosive();
                CheckForPatrol();
                break;

            case BossState.Patrolling:
                FaceTarget(_player.position);
                break;

            case BossState.Throwing:
                FaceTarget(_player.position);
                break;
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Z-Axis Lock
    // ───────────────────────────────────────────────────────────────────────
    private void EnforceZAxis()
    {
        if (!_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return;
        if (_state == BossState.Throwing) return;

        Vector3 pos = transform.position;
        if (Mathf.Abs(pos.z - lockedZ) > 0.15f)
        {
            Debug.Log($"[Boss] EnforceZAxis: correcting Z drift ({pos.z:F3} → {lockedZ:F3})");
            _agent.Warp(new Vector3(pos.x, pos.y, lockedZ));
        }
    }

    private void StrafeTowardPlayer()
    {
        if (!_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return;
        if (_state == BossState.Throwing) return;

        float targetX = Mathf.Clamp(_player.position.x,
                                     _spawnPos.x - strafeRange,
                                     _spawnPos.x + strafeRange);
        _agent.SetDestination(new Vector3(targetX, transform.position.y, lockedZ));
    }

    // ───────────────────────────────────────────────────────────────────────
    //  State Transitions
    // ───────────────────────────────────────────────────────────────────────
    private void EnterEngage()
    {
        if (_state == BossState.Idle)
        {
            FightStarted = true;
            healthBarUI?.Initialize(maxHealth, bossName);
            StartBattleMusic();
            arenaWall?.ActivateWalls();
            foreach (var part in parts)
                if (part != null) part.HighlightPart();
        }

        _state = BossState.Engaging;
        _agent.speed     = chaseSpeed;
        _agent.isStopped = false;
        bossAnimator?.SetBool("isWalking", true);
    }

    private void CheckForPatrol()
    {
        float hpPct = _currentHealth / maxHealth;
        if (hpPct > patrolHealthPct) return;
        // FIX: guard uses _lastFireTime (shoot timer only).
        // Previously this was accidentally gated on the throw timer too.
        if (Time.time - _lastFireTime < 2f) return;
        StartPatrolCycle();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Patrol
    // ───────────────────────────────────────────────────────────────────────
    private void StartPatrolCycle()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            Debug.LogWarning("[Boss] No patrol points assigned — staying in Engage.");
            EnterEngage();
            return;
        }

        if (_patrolCoroutine != null) StopCoroutine(_patrolCoroutine);

        _state           = BossState.Patrolling;
        _agent.speed     = patrolSpeed;
        _agent.isStopped = false;
        bossAnimator?.SetBool("isWalking", true);
        _patrolCoroutine = StartCoroutine(PatrolCycleCoroutine());
    }

    private IEnumerator PatrolCycleCoroutine()
    {
        while (_state != BossState.Dead)
        {
            Transform targetPoint = patrolPoints[_currentPatrolIndex];
            Debug.Log($"[Boss] Patrol → point {_currentPatrolIndex}: {targetPoint.position}");

            _agent.isStopped = false;
            _agent.SetDestination(targetPoint.position);

            yield return null;
            yield return null;
            yield return null;

            yield return new WaitUntil(() =>
                _state == BossState.Dead ||
                (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance));

            if (_state == BossState.Dead) yield break;

            Debug.Log($"[Boss] Arrived at patrol point {_currentPatrolIndex}");
            _agent.isStopped = true;
            bossAnimator?.SetBool("isWalking", false);

            float timer = 0f;
            while (timer < timeAtPoint && _state != BossState.Dead)
            {
                FaceTarget(_player.position);
                TryFireBurst();
                TryThrowExplosive();
                timer += Time.deltaTime;
                yield return null;
            }

            if (_state == BossState.Dead) yield break;

            _currentPatrolIndex = (_currentPatrolIndex + 1) % patrolPoints.Length;
            bossAnimator?.SetBool("isWalking", true);
        }
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Shooting  —  _lastFireTime is ONLY touched here
    // ───────────────────────────────────────────────────────────────────────
    private void TryFireBurst()
    {
        if (Time.time - _lastFireTime < fireRate) return;
        if (!HasLineOfSight()) return;
        _lastFireTime = Time.time;   // shoot timer — does NOT affect throw
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

    private Vector3 GetFireOrigin() =>
        transform.position + Vector3.up * 1.8f + transform.forward * 0.5f;

    private void SpawnBullet()
    {
        if (!bulletPrefab || _player == null) return;

        Vector3 origin    = GetFireOrigin();
        Vector3 targetPos = _player.position + Vector3.up * 0.8f;
        Vector3 dir       = (targetPos - origin).normalized;

        float minY = Mathf.Sin(-30f * Mathf.Deg2Rad);
        if (dir.y < minY) dir = new Vector3(dir.x, minY, dir.z).normalized;
        dir = ApplyInaccuracy(dir);

        var go     = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(dir));
        var bullet = go.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.baseDamage = 15f;
            bullet.Initialize(dir, fromEnemy: true, owner: gameObject);
        }
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
        Vector3 origin = GetFireOrigin();
        Vector3 to     = _player.position + Vector3.up * 0.8f;
        return !Physics.Linecast(origin, to, LayerMask.GetMask("Default", "Environment"));
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Explosives  —  _lastThrowTime is ONLY touched here
    // ───────────────────────────────────────────────────────────────────────
    private void TryThrowExplosive()
    {
        if (_partsDestroyed < grenadesUnlockAtPartsDestroyed) return;
        if (Time.time - _lastThrowTime < throwCooldown)       return;  // throw timer — does NOT affect burst
        if (_state == BossState.Throwing)                     return;

        _lastThrowTime = Time.time;   // throw timer — does NOT affect burst
        if (_throwCoroutine != null) StopCoroutine(_throwCoroutine);
        _throwCoroutine = StartCoroutine(ThrowCoroutine());
    }

    private IEnumerator ThrowCoroutine()
    {
        _state = BossState.Throwing;
        _agent.isStopped = true;
        FaceTarget(_player.position);
        bossAnimator?.SetTrigger("attack");

        yield return new WaitForSeconds(0.6f);

        if (explosivePrefab == null)
        {
            Debug.LogError("[Boss] explosivePrefab not assigned in Inspector.");
        }
        else if (throwOrigin == null)
        {
            Debug.LogError("[Boss] throwOrigin not assigned in Inspector.");
        }
        else
        {
            Vector3 targetPos = _player.position;
            var go   = Instantiate(explosivePrefab, throwOrigin.position, Quaternion.identity);
            var bomb = go.GetComponent<BossExplosive>();

            if (bomb != null)
            {
                bomb.Launch(targetPos);
                Debug.Log($"[Boss] Bomb thrown toward {targetPos}");
            }
            else
            {
                Debug.LogError("[Boss] explosivePrefab missing BossExplosive component!");
            }
        }

        yield return new WaitForSeconds(0.4f);

        _agent.isStopped = false;
        _throwCoroutine  = null;

        if (_state == BossState.Dead) yield break;

        if (_currentHealth / maxHealth <= patrolHealthPct)
            StartPatrolCycle();
        else
            EnterEngage();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Parts
    // ───────────────────────────────────────────────────────────────────────
    private void HandlePartDestroyed(BossPart part)
    {
        if (!parts.Contains(part)) return;

        _partsAlive--;
        _partsDestroyed++;

        Debug.Log($"[Boss] Part destroyed. partsDestroyed={_partsDestroyed} "
                + $"/ unlock bombs at {grenadesUnlockAtPartsDestroyed}");

        if (_partsAlive <= 0)
        {
            _currentHealth = 0;
            healthBarUI?.UpdateHealth(0, 0);
            Die();
            return;
        }

        float partDamage = maxHealth * 0.15f;
        _currentHealth   = Mathf.Max(0f, _currentHealth - partDamage);
        healthBarUI?.UpdateHealth(_currentHealth, _partsAlive);

        fireRate      = Mathf.Max(0.2f, fireRate      - 0.15f);
        throwCooldown = Mathf.Max(1.5f, throwCooldown - 1.5f);

        StartPatrolCycle();
    }

    // ───────────────────────────────────────────────────────────────────────
    //  IDamageable
    // ───────────────────────────────────────────────────────────────────────
    public bool IsAlive => _state != BossState.Dead;

    public void TakeDamage(float amount, GameObject source = null)
    {
        if (_state == BossState.Dead) return;

        float reduction = _partsAlive * 0.08f;
        float actual    = amount * (1f - reduction);
        _currentHealth  = Mathf.Max(0f, _currentHealth - actual);

        Debug.Log($"[Boss] TakeDamage: {actual:F1} | HP={_currentHealth:F1}");
        healthBarUI?.UpdateHealth(_currentHealth, _partsAlive);

        if (_currentHealth <= 0f) Die();
    }

    private void Die()
    {
        if (_patrolCoroutine != null) { StopCoroutine(_patrolCoroutine); _patrolCoroutine = null; }
        if (_throwCoroutine  != null) { StopCoroutine(_throwCoroutine);  _throwCoroutine  = null; }

        _state           = BossState.Dead;
        _agent.isStopped = true;
        bossAnimator?.SetBool("isDead", true);
        healthBarUI?.Hide();
        StopBattleMusic();
        arenaWall?.DeactivateWalls();
        arenaBridge?.SpawnBridge();
        if (elevator != null) ActivateElevator();
        Debug.Log("[Boss] DEFEATED.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Audio
    // ───────────────────────────────────────────────────────────────────────
    private void StartBattleMusic()
    {
        if (_battleMusicActive) return;
        _battleMusicActive = true;
        AudioManager.Instance?.PlayMusic(AudioManager.MusicState.Boss);
    }

    private void StopBattleMusic()
    {
        if (!_battleMusicActive) return;
        _battleMusicActive = false;
        AudioManager.Instance?.PlayMusic(AudioManager.MusicState.Main);
    }

    // ───────────────────────────────────────────────────────────────────────
    //  IResettable
    // ───────────────────────────────────────────────────────────────────────
    public string ResettableID => gameObject.name;
    public void SaveInitialState() { }

    public void ResetState()
    {
        if (_patrolCoroutine != null) { StopCoroutine(_patrolCoroutine); _patrolCoroutine = null; }
        if (_throwCoroutine  != null) { StopCoroutine(_throwCoroutine);  _throwCoroutine  = null; }

        _state              = BossState.Idle;
        FightStarted        = false;
        _currentHealth      = maxHealth;
        _partsAlive         = 5;
        _partsDestroyed     = 0;
        fireRate            = 0.8f;
        throwCooldown       = 6f;
        _lastFireTime       = 0f;
        _lastThrowTime      = 0f;
        _battleMusicActive  = false;
        _currentPatrolIndex = 0;

        if (_agent.isActiveAndEnabled && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.ResetPath();
            _agent.Warp(_spawnPos);
        }

        transform.rotation = _spawnRot;
        _agent.isStopped   = false;

        foreach (var part in parts) part.ResetPart();

        healthBarUI?.Hide();
        StopBattleMusic();
        arenaWall?.DeactivateWalls();
        arenaBridge?.ResetBridge();

        bossAnimator?.SetBool("isDead",    false);
        bossAnimator?.SetBool("isWalking", false);

        Debug.Log("[Boss] ResetState complete.");
    }

    // ───────────────────────────────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────────────────────────────
    private void FaceTarget(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                8f * Time.deltaTime);
    }

    private void ActivateElevator()
    {
        elevator.SetActive(true);
        PlatformMoving plat = elevator.GetComponent<PlatformMoving>();
        if (plat) plat.StartMovement();
    }
}
