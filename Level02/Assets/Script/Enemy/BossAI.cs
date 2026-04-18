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
    [SerializeField] private string bossName       = "UNIT-7 OVERSEER";
    [SerializeField] private float  maxHealth      = 500f;
    [SerializeField] private float  coverHealthPct = 0.70f;

    [Header("Shooting")]
    [SerializeField] private Transform  muzzlePoint;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float      fireRate   = 0.8f;
    [SerializeField] private int        burstCount = 3;
    [SerializeField] private float      accuracy   = 0.75f;

    [Header("Explosives")]
    [SerializeField] private Transform  throwOrigin;
    [SerializeField] private GameObject explosivePrefab;
    [SerializeField] private float      throwForce    = 12f;
    [SerializeField] private float      throwCooldown = 8f;

    [Header("Movement")]
    [SerializeField] private float chaseSpeed     = 4f;
    [SerializeField] private float coverSpeed     = 6f;
    [SerializeField] private float engageDistance = 18f;

    [Header("Parts")]
    [SerializeField] private List<BossPart> parts = new();

    [Header("UI")]
    [SerializeField] private BossHealthBarUI healthBarUI;

    [Header("Animation")]
    [SerializeField] private Animator bossAnimator;

   // ─────────────────────── Audio ───────────────────────────
    private bool _battleMusicActive = false; 

    // ─────────────────────── State ───────────────────────────
    private enum BossState { Idle, Engaging, TakingCover, Throwing, Dead }
    private BossState _state = BossState.Idle;

    private float           _currentHealth;
    private NavMeshAgent    _agent;
    private BossCoverSystem _cover;
    private Transform       _player;
    private float           _lastFireTime;
    private float           _lastThrowTime;
    private int             _partsAlive = 5;
    private Vector3         _spawnPos;
    private Quaternion      _spawnRot;

    [Header("Arena")]
[SerializeField] private BossArenaWall arenaWall;
[SerializeField] private BossBridge    arenaBridge;
[Header("UI")]
[SerializeField] private GameObject      bossHUDObject;
public bool FightStarted { get; private set; } = false;

    // ─────────────────────── Lifecycle ───────────────────────
   private void Awake()
{
    _agent    = GetComponent<NavMeshAgent>();
    _cover    = GetComponent<BossCoverSystem>();
    _player   = GameObject.FindGameObjectWithTag("Player")?.transform;
    _spawnPos = transform.position;
    _spawnRot = transform.rotation;
    InitHealthBar();

    // Subscribe here — guaranteed to run regardless of active state
    BossPart.OnPartDestroyed += HandlePartDestroyed;
}
private void InitHealthBar()
{
    if (bossHUDObject != null)
        healthBarUI = bossHUDObject.GetComponent<BossHealthBarUI>();
}
private void OnDestroy()
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

        float dist = Vector3.Distance(transform.position, _player.position);

        switch (_state)
        {
            case BossState.Idle:
                if (dist < engageDistance) EnterEngage();
                break;

            case BossState.Engaging:
                FaceTarget(_player.position);
                _agent.SetDestination(_player.position);
                TryFireBurst();
                TryThrowExplosive();
                CheckForCover();
                break;

            case BossState.TakingCover:
                TryFireBurst();
                if (!_agent.pathPending && _agent.remainingDistance < 0.6f)
                    EnterEngage();
                break;

            case BossState.Throwing:
                break;
        }
    }

    // ─────────────────────── State Transitions ───────────────
  private void EnterEngage()
{
    if (_state == BossState.Idle)
    {
        FightStarted = true;
        healthBarUI?.Initialize(maxHealth, bossName);
        StartBattleMusic();
        arenaWall?.ActivateWalls();
    }

    _state = BossState.Engaging;
    _agent.speed = chaseSpeed;
    bossAnimator?.SetBool("isWalking", true);
}
private void StartBattleMusic()
{
    if (_battleMusicActive) return;
    _battleMusicActive = true;
    AudioManager.Instance?.PlayMusic(AudioManager.MusicState.Battle);
    Debug.Log("[Boss] Battle music started.");
}

private void StopBattleMusic()
{
    if (!_battleMusicActive) return;
    _battleMusicActive = false;
    AudioManager.Instance?.PlayMusic(AudioManager.MusicState.Main);
    Debug.Log("[Boss] Battle music stopped.");
}
    private void CheckForCover()
    {
        float hpPct = _currentHealth / maxHealth;
        if (hpPct > coverHealthPct) return;
        if (Time.time - _lastFireTime < 2f) return;

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

// Calculates a safe chest-height origin — no bone/Transform dependency
private Vector3 GetFireOrigin()
{
    // Always 1.8 units above the boss root, pushed 0.5 forward
    return transform.position
           + Vector3.up * 1.8f
           + transform.forward * 0.5f;
}

private void SpawnBullet()
{
    if (!bulletPrefab || _player == null) return;

    Vector3 origin    = GetFireOrigin();
    Vector3 targetPos = _player.position + Vector3.up * 0.8f;
    Vector3 dir       = (targetPos - origin).normalized;

    // Hard clamp — never fire below -30 degrees
    float minY = Mathf.Sin(-30f * Mathf.Deg2Rad);
    if (dir.y < minY)
        dir = new Vector3(dir.x, minY, dir.z).normalized;

    dir = ApplyInaccuracy(dir);

    var go     = Instantiate(bulletPrefab, origin, Quaternion.LookRotation(dir));
    var bullet = go.GetComponent<Bullet>();

    if (bullet != null)
    {
        // Use your existing Bullet.Initialize — same as EnemyAI
        bullet.baseDamage = 15f;
        bullet.Initialize(dir, fromEnemy: true, owner: gameObject);
    }
    else if (go.TryGetComponent<Rigidbody>(out var rb) && !rb.isKinematic)
    {
        // Fallback only if no Bullet.cs and non-kinematic
        rb.linearVelocity = dir * 40f;
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
    return !Physics.Linecast(origin, to,
                             LayerMask.GetMask("Default", "Environment"));
}

    // ─────────────────────── Explosives ──────────────────────
    private void TryThrowExplosive()
    {
        if (Time.time - _lastThrowTime < throwCooldown) return;
        if (_partsAlive > 3) return;

        _lastThrowTime = Time.time;
        StartCoroutine(ThrowCoroutine());
    }

    private IEnumerator ThrowCoroutine()
    {
        _state = BossState.Throwing;
        _agent.isStopped = true;
        FaceTarget(_player.position);
        bossAnimator?.SetTrigger("attack");

        yield return new WaitForSeconds(0.6f);

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
    if (!parts.Contains(part))
    {
        Debug.LogWarning($"[Boss] Part {part.name} destroyed but NOT in parts list!");
        return;
    }

    _partsAlive--;
    float partDamage = maxHealth * 0.15f;
    _currentHealth   = Mathf.Max(0f, _currentHealth - partDamage);

    Debug.Log($"[Boss] HandlePartDestroyed: partsAlive={_partsAlive} HP={_currentHealth}");

    healthBarUI?.UpdateHealth(_currentHealth, _partsAlive);

    fireRate      = Mathf.Max(0.3f, fireRate      - 0.08f);
    throwCooldown = Mathf.Max(3f,   throwCooldown - 1f);

    if (_currentHealth <= 0f) { Die(); return; }

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
    float actual    = amount * (1f - reduction);
    _currentHealth -= actual;
    _currentHealth  = Mathf.Max(0f, _currentHealth);

    Debug.Log($"[Boss] TakeDamage: raw={amount} actual={actual} HP={_currentHealth} parts={_partsAlive}");

    healthBarUI?.UpdateHealth(_currentHealth, _partsAlive);

    if (_currentHealth <= 0f)
    {
        Debug.Log("[Boss] HP reached zero — calling Die()");
        Die();
    }
}
   private void Die()
{
    _state = BossState.Dead;
    _agent.isStopped = true;
    bossAnimator?.SetBool("isDead", true);
    healthBarUI?.Hide();
    StopBattleMusic();

    Debug.Log($"[Boss] Die() called. arenaWall assigned: {arenaWall != null}");
    arenaWall?.DeactivateWalls();
    arenaBridge?.SpawnBridge();

    Debug.Log("[Boss] DEFEATED.");
}
    // ─────────────────────── IResettable ─────────────────────
    public string ResettableID => gameObject.name;

    public void SaveInitialState() { }

 public void ResetState()
{
    Debug.Log($"[Boss] ResetState called. arenaWall={arenaWall != null} | FightStarted={FightStarted}");
    
    FightStarted   = false;
    _state         = BossState.Idle;
    _currentHealth = maxHealth;
    _partsAlive    = 5;
    fireRate       = 0.8f;
    throwCooldown  = 8f;

    transform.SetPositionAndRotation(_spawnPos, _spawnRot);
    _agent.isStopped = false;
    _agent.Warp(_spawnPos);

    foreach (var part in parts) part.ResetPart();

    healthBarUI?.Hide();
    StopBattleMusic();
    _battleMusicActive = false;

    if (arenaWall != null)
    {
        Debug.Log("[Boss] Calling DeactivateWalls...");
        arenaWall.DeactivateWalls();
    }
    else
    {
        Debug.LogWarning("[Boss] arenaWall is NULL in ResetState!");
        // Force find it directly
        BossArenaWall wall = FindObjectOfType<BossArenaWall>();
        if (wall != null)
        {
            Debug.Log("[Boss] Found wall via FindObjectOfType — deactivating.");
            wall.DeactivateWalls();
            arenaWall = wall; // cache it for next time
        }
        else
        {
            Debug.LogError("[Boss] BossArenaWall not found anywhere in scene!");
        }
    }

    arenaBridge?.ResetBridge();

    bossAnimator?.SetBool("isDead",    false);
    bossAnimator?.SetBool("isWalking", false);
}
    // ─────────────────────── Helpers ─────────────────────────
    private void FaceTarget(Vector3 target)
    {
    // Only rotate on Y axis — prevents boss tilting forward/backward on slopes
    Vector3 dir = target - transform.position;
    dir.y = 0f;
    if (dir.sqrMagnitude > 0.01f)
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(dir),
            8f * Time.deltaTime);
    }
}