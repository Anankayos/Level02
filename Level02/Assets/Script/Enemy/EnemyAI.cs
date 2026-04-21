using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Collider))]
public class EnemyAI : MonoBehaviour, IDamageable, IResettable
{
    private enum State { Patrol, Suspicious, Search, Combat, Melee, Dead }

    // ═══ Inspector ═══════════════════════════════════════════════

    [Header("Vision")]
    [SerializeField] private float     visionRange      = 15f;
    [SerializeField, Range(10, 180)]
                     private float     visionAngle      = 65f;
    [SerializeField] private float     alertVisionAngle = 130f;
    [SerializeField] private float     peripheralRange  = 3f;
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Hearing")]
    [SerializeField] private float hearingRange = 10f;

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Combat")]
    [SerializeField] private float     shootingRange = 18f;
    [SerializeField] private float     meleeRange    = 2f;
    [SerializeField] private float     shootCooldown = 1.8f;
    [SerializeField] private float     shootDamage   = 10f;
    [SerializeField] private float     meleeDamage   = 22f;
    [SerializeField] private float     meleeCooldown = 1.6f;
    [SerializeField] private Transform weaponMuzzle;

    [Tooltip("Assign the BulletPrefab (Bullet.cs). If left empty, falls back to hitscan.")]
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField, Range(0f, 1f)] private float accuracy = 0.60f;

    [Header("Floor Separation")]
    [Tooltip("Max Y difference between enemy and player to allow vision/shooting.")]
    [SerializeField] private float maxFloorHeightDiff = 2.5f;

    [Header("Bullet Layer Mask")]
    [Tooltip("Layers bullets and hitscan CAN hit. Exclude NavMesh, Triggers, Ignore Raycast.")]
    [SerializeField] private LayerMask shootMask = Physics.DefaultRaycastLayers;

    [Header("Patrol")]
    [SerializeField] private Transform[] waypoints;
    [SerializeField] private float       waypointWait = 2f;
    [SerializeField] private float       patrolSpeed  = 2f;
    [SerializeField] private float       chaseSpeed   = 5f;

    [Header("Suspicion")]
    [SerializeField] private float suspicionRate      = 35f;
    [SerializeField] private float suspicionDecay     = 12f;
    [SerializeField] private float suspicionThreshold = 100f;

    [Header("Search")]
    [SerializeField] private float searchDuration = 10f;

    [Header("Zone (optional)")]
    [SerializeField] private PatrolZone patrolZone;

    // ═══ Runtime ════════════════════════════════════════════════

    private NavMeshAgent _agent;
    private Animator     _anim;
    private GameObject   _player;
    private WeaponSFX    _weaponSFX;
    private State        _state;
    private bool         _ready;

    private float _hp;
    private Vector3 _lkp;
    private bool    _hasLKP;

    private int   _wpIndex;
    private float _wpTimer;
    private bool  _wpWaiting;
    private bool  _wpReady;

    private float _suspicion;
    private float _suspLookTimer;
    private bool  _suspMoving;

    private float _searchTimer;

    private bool  isInCombat;
    public  bool  IsInCombat => isInCombat;
    private float       _nextShot;
    private float       _lostSight;
    private const float LostSightTimeout = 3f;

    // Non-static to avoid cross-scene leaks
    private int  _globalEnemiesInCombat = 0;
    private bool _contributingToMusic   = false;

    private float _nextMelee;

    private string     _id;
    private Vector3    _initPos;
    private Quaternion _initRot;

    public string ResettableID =>
        string.IsNullOrEmpty(_id) ? (_id = System.Guid.NewGuid().ToString()) : _id;

    // ═══ Unity Messages ══════════════════════════════════════════

    private void Awake()
    {
        _agent     = GetComponent<NavMeshAgent>();
        _anim      = GetComponent<Animator>();
        _player    = GameObject.FindWithTag("Player");
        _weaponSFX = GetComponent<WeaponSFX>();
        _hp        = maxHealth;
        _initPos   = transform.position;
        _initRot   = transform.rotation;

        _agent.enabled = false;
        NoiseEmitter.OnNoiseEmitted += OnNoiseHeard;
    }

    private IEnumerator Start()
    {
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            transform.position = hit.position;

        yield return new WaitForEndOfFrame();
        _agent.enabled = true;
        yield return new WaitForEndOfFrame();

        if (!_agent.isOnNavMesh)
        {
            Debug.LogError($"[EnemyAI '{name}'] Not on NavMesh.");
            yield break;
        }

        _ready = true;
        SetState(State.Patrol);
    }

    private void Update()
    {
        if (!_ready || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return;

        if (_state == State.Patrol)
            _suspicion = Mathf.MoveTowards(_suspicion, 0f, suspicionDecay * Time.deltaTime);

        switch (_state)
        {
            case State.Patrol:     TickPatrol();     break;
            case State.Suspicious: TickSuspicious(); break;
            case State.Search:     TickSearch();     break;
            case State.Combat:     TickCombat();     break;
            case State.Melee:      TickMelee();      break;
        }
    }

    // ═══ State Machine ═══════════════════════════════════════════

    private void SetState(State next)
    {
        ExitState(_state);
        _state = next;
        EnterState(_state);

        EnemyAlertLevel level = next switch
        {
            State.Suspicious or State.Search => EnemyAlertLevel.Suspicious,
            State.Combat     or State.Melee  => EnemyAlertLevel.Alerted,
            _                                => EnemyAlertLevel.None
        };
        GameEvents.FireEnemyAlertChanged(level);
        UpdateMusicState(next);
    }

    private void UpdateMusicState(State currentState)
    {
        bool activeCombat = currentState is State.Combat or State.Melee;
        if (activeCombat && !_contributingToMusic)
        {
            _contributingToMusic = true;
            _globalEnemiesInCombat++;
            AudioManager.Instance?.PlayMusic(AudioManager.MusicState.Battle);
        }
        else if (!activeCombat && _contributingToMusic)
        {
            _contributingToMusic = false;
            _globalEnemiesInCombat = Mathf.Max(0, _globalEnemiesInCombat - 1);
            if (_globalEnemiesInCombat <= 0)
                AudioManager.Instance?.PlayMusic(AudioManager.MusicState.Main);
        }
    }

    private void EnterState(State s)
    {
        switch (s)
        {
            case State.Patrol:
                _agent.speed = patrolSpeed;
                _wpWaiting   = false;
                _wpReady     = false;
                break;

            case State.Suspicious:
                _agent.speed   = patrolSpeed * 0.75f;
                _suspLookTimer = 3.5f;
                _suspMoving    = _hasLKP;
                _anim?.SetBool("IsAlert", true);
                if (_hasLKP) Move(_lkp);
                break;

            case State.Search:
                _agent.speed = chaseSpeed * 0.7f;
                _searchTimer = searchDuration;
                _anim?.SetBool("IsAlert",     true);
                _anim?.SetBool("IsSearching", true);
                if (_hasLKP) Move(_lkp);
                break;

            case State.Combat:
                isInCombat   = true;
                _agent.speed = chaseSpeed;
                _lostSight   = 0f;
                _nextShot    = Time.time + 0.6f;
                _anim?.SetBool("IsInCombat", true);
                break;

            case State.Melee:
                isInCombat   = true;
                _agent.speed = chaseSpeed;
                _nextMelee   = Time.time + 0.4f;
                _anim?.SetBool("IsMelee", true);
                break;

            case State.Dead:
                isInCombat = false;
                _agent.ResetPath();
                _agent.enabled                   = false;
                GetComponent<Collider>().enabled = false;
                _anim?.SetTrigger("Die");
                _anim?.SetBool("IsDead", true);
                SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);
                break;
        }
    }

    private void ExitState(State s)
    {
        switch (s)
        {
            case State.Suspicious:
                _anim?.SetBool("IsAlert",  false);
                _anim?.SetBool("IsMoving", false);
                break;

            case State.Search:
                _anim?.SetBool("IsAlert",     false);
                _anim?.SetBool("IsSearching", false);
                break;

            case State.Combat:
                isInCombat = false;
                _agent.ResetPath();
                _anim?.SetBool("IsInCombat", false);
                _anim?.SetBool("IsMoving",   false);
                break;

            case State.Melee:
                isInCombat = false;
                _anim?.SetBool("IsMelee", false);
                break;

            case State.Dead:
                _agent.enabled                   = true;
                GetComponent<Collider>().enabled = true;
                _anim?.SetBool("IsDead", false);
                break;
        }
    }

    // ═══ State Ticks ═════════════════════════════════════════════

    private void TickPatrol()
    {
        if (!_wpReady) { _wpReady = true; MoveToWaypoint(); return; }
        if (CanSeePlayer()) { StampLKP(); SetState(State.Combat); return; }
        if (waypoints.Length == 0) return;

        if (_wpWaiting)
        {
            _wpTimer -= Time.deltaTime;
            _anim?.SetBool("IsMoving", false);
            if (_wpTimer <= 0f)
            {
                _wpWaiting = false;
                _wpIndex   = (_wpIndex + 1) % waypoints.Length;
                MoveToWaypoint();
            }
            return;
        }

        if (!_agent.pathPending && _agent.remainingDistance < 0.4f)
        {
            _wpWaiting = true;
            _wpTimer   = waypointWait;
        }
    }

    private void TickSuspicious()
    {
        if (CanSeePlayer()) { StampLKP(); SetState(State.Combat); return; }

        _suspicion += suspicionRate * Time.deltaTime;
        if (_suspicion >= suspicionThreshold) { SetState(State.Search); return; }

        if (_suspMoving && !_agent.pathPending && _agent.remainingDistance < 0.6f)
        {
            _suspMoving = false;
            _anim?.SetBool("IsMoving", false);
        }

        if (!_suspMoving)
        {
            _suspLookTimer -= Time.deltaTime;
            transform.Rotate(Vector3.up, 45f * Time.deltaTime);
            if (_suspLookTimer <= 0f) { _suspicion = 0f; SetState(State.Patrol); }
        }
    }

    private void TickSearch()
    {
        if (CanSeePlayer()) { StampLKP(); SetState(State.Combat); return; }
        _searchTimer -= Time.deltaTime;
        if (_searchTimer <= 0f) { _hasLKP = false; _suspicion = 0f; SetState(State.Patrol); }
    }

    private void TickCombat()
    {
        if (!PlayerInZone())
        {
            _hasLKP    = false;
            _suspicion = 0f;
            SetState(State.Patrol);
            return;
        }

        bool sees = CanSeePlayer();

        if (sees)
        {
            StampLKP();
            _lostSight = 0f;

            float dist = Dist(_player.transform.position);
            if (dist <= meleeRange) { SetState(State.Melee); return; }

            if      (dist > shootingRange * 0.75f) Move(_lkp);
            else if (dist < shootingRange * 0.4f)  Move(transform.position +
                         (transform.position - _player.transform.position).normalized * 4f);
            else                                   _agent.ResetPath();

            FaceTarget(_player.transform.position);

            if (Time.time >= _nextShot) { Shoot(); _nextShot = Time.time + shootCooldown; }
        }
        else if (_hasLKP)
        {
            _lostSight += Time.deltaTime;
            Move(_lkp);
            FaceTarget(_lkp);

            if (Dist(_lkp) <= shootingRange && Time.time >= _nextShot)
            {
                ShootAtPosition(_lkp + Vector3.up * 0.9f);
                _nextShot = Time.time + shootCooldown * 1.4f;
            }

            if (_lostSight >= LostSightTimeout) SetState(State.Search);
        }
        else
        {
            SetState(State.Search);
        }

        _anim?.SetBool("IsMoving", _agent.velocity.magnitude > 0.2f);
    }

    private void TickMelee()
    {
        if (_player == null) return;

        FaceTarget(_player.transform.position);
        float dist = Dist(_player.transform.position);

        if (dist <= meleeRange)
        {
            Move(_player.transform.position);
            if (Time.time >= _nextMelee)
            {
                _anim?.SetTrigger("MeleeAttack");
                StartCoroutine(DelayedMeleeDamage());
                _nextMelee = Time.time + meleeCooldown;
            }
        }
        else if (dist > meleeRange + 1f)
        {
            SetState(State.Combat);
        }
    }

    // ═══ Vision ══════════════════════════════════════════════════

    private bool CanSeePlayer()
    {
        if (_player == null) return false;
        if (patrolZone != null && !patrolZone.Contains(_player.transform.position)) return false;

        // Y check FIRST — cheapest rejection, avoids SphereCast on wrong floor
        float yDiff = Mathf.Abs(_player.transform.position.y - transform.position.y);
        if (yDiff > maxFloorHeightDiff) return false;

        Vector3 toPlayer = _player.transform.position - transform.position;
        float   dist     = toPlayer.magnitude;

        if (dist <= peripheralRange) return LineOfSight(_player.transform.position);

        float cone = (_state is State.Combat or State.Search) ? alertVisionAngle : visionAngle;
        if (dist > visionRange) return false;
        if (Vector3.Angle(transform.forward, toPlayer.normalized) > cone * 0.5f) return false;

        return LineOfSight(_player.transform.position);
    }

    private bool LineOfSight(Vector3 target)
    {
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Vector3 dst = target + Vector3.up * 0.9f;
        Vector3 dir = dst - eye;

        return !Physics.SphereCast(eye, 0.15f, dir.normalized, out _, dir.magnitude, obstacleLayer);
    }

    // ═══ Hearing ════════════════════════════════════════════════

    private void OnNoiseHeard(Vector3 pos, float radius, NoiseType type, GameObject source)
    {
        if (source == gameObject) return;
        if (_state == State.Dead || !_ready) return;
        if (patrolZone != null && !patrolZone.Contains(pos)) return;

        float range = hearingRange + radius;
        if (type == NoiseType.Gunshot) range *= 2.2f;
        if (Vector3.Distance(transform.position, pos) > range) return;

        switch (_state)
        {
            case State.Patrol:
                _lkp    = pos;
                _hasLKP = true;
                SetState(State.Suspicious);
                break;
            case State.Suspicious:
            case State.Search:
                _lkp        = pos;
                _suspicion += 40f;
                break;
        }
    }

    // ═══ IDamageable ═════════════════════════════════════════════

    public bool IsAlive   => _hp > 0f;
    public bool IsAlerted => _state is State.Combat or State.Melee;

    public void ExecuteStealthKill()
    {
        if (!IsAlive) return;
        _anim?.SetTrigger("StealthKilled");
        TakeDamage(9999f, null);
    }

    public void TakeDamage(float amount, GameObject source = null)
    {
        if (!IsAlive) return;
        _hp -= amount;
        _anim?.SetTrigger("Hit");

        bool sourceInZone = true;
        if (source != null)
        {
            _lkp    = source.transform.position;
            _hasLKP = true;
            if (patrolZone != null && !patrolZone.Contains(_lkp))
                sourceInZone = false;
        }

        if (_hp <= 0f)
        {
            bool isStealthKill = _state == State.Patrol    ||
                                 _state == State.Suspicious ||
                                 _state == State.Search;

            if (isStealthKill) GameEvents.FireStealthKill();
            else               GameEvents.FireHit(HitType.Kill);

            SetState(State.Dead);
        }
        else
        {
            // ── Fire non-lethal hitmarker ──
            GameEvents.FireHit(HitType.Enemy);

            if (_state is not (State.Combat or State.Melee))
            {
                if (sourceInZone) SetState(State.Combat);
                else              SetState(State.Search);
            }
        }
    }

    // ═══ IResettable ═════════════════════════════════════════════

    public void SaveInitialState() { }

    public void ResetState()
    {
        _hp        = maxHealth;
        _suspicion = 0f;
        _hasLKP    = false;
        _ready     = false;
        isInCombat = false;

        if (_contributingToMusic)
        {
            _contributingToMusic   = false;
            _globalEnemiesInCombat = Mathf.Max(0, _globalEnemiesInCombat - 1);
            if (_globalEnemiesInCombat <= 0)
                AudioManager.Instance?.PlayMusic(AudioManager.MusicState.Main);
        }

        gameObject.SetActive(true);
        GetComponent<Collider>().enabled = true;
        StartCoroutine(ResetCoroutine());
    }

    private IEnumerator ResetCoroutine()
    {
        _agent.enabled = false;
        transform.SetPositionAndRotation(_initPos, _initRot);
        yield return new WaitForEndOfFrame();
        _agent.enabled = true;
        yield return new WaitForEndOfFrame();
        if (_agent.isOnNavMesh) { _ready = true; SetState(State.Patrol); }
    }

    public void ForceKill()
    {
        _hp = 0f;
        if (_state != State.Dead) SetState(State.Dead);
    }

    // ═══ Utilities ═══════════════════════════════════════════════

    private void MoveToWaypoint()
    {
        if (waypoints.Length == 0) return;
        Move(waypoints[_wpIndex].position);
        _anim?.SetBool("IsMoving", true);
    }

    private void Move(Vector3 dest)
    {
        if (!_agent.isActiveAndEnabled || !_agent.isOnNavMesh) return;
        if (patrolZone != null && !patrolZone.Contains(dest))
            patrolZone.TryClampDestination(dest, out dest);
        _agent.SetDestination(dest);
    }

    private void FaceTarget(Vector3 target)
    {
        Vector3 dir = (target - transform.position).normalized;
        dir.y = 0f;
        if (dir == Vector3.zero) return;
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 12f);
    }

    private void StampLKP()
    {
        if (_player == null) return;
        _lkp    = _player.transform.position;
        _hasLKP = true;
    }

    private bool PlayerInZone()
    {
        if (patrolZone == null || _player == null) return true;
        return patrolZone.Contains(_player.transform.position);
    }

    private float Dist(Vector3 p) => Vector3.Distance(transform.position, p);

    // ═══ Shooting ════════════════════════════════════════════════

    private void Shoot()
    {
        if (_player == null) return;

        if (weaponMuzzle == null)
        {
            var auto = new GameObject("_AutoMuzzle");
            auto.transform.SetParent(transform);
            auto.transform.localPosition = new Vector3(0f, 1.4f, 0.5f);
            weaponMuzzle = auto.transform;
            Debug.LogWarning($"[{name}] weaponMuzzle not assigned — auto-created.");
        }

        _anim?.SetTrigger("Shoot");

        if (bulletPrefab != null) ShootBullet();
        else                      ShootHitscan();

        _weaponSFX?.PlayShot();
        NoiseEmitter.EmitNoise(weaponMuzzle.position, 35f, NoiseType.Gunshot, gameObject);
    }

    private void ShootBullet()
    {
        float yDiff = Mathf.Abs(_player.transform.position.y - weaponMuzzle.position.y);
        if (yDiff > maxFloorHeightDiff) return;

        Vector3 target = _player.transform.position + Vector3.up * 0.9f;
        Vector3 dir    = (target - weaponMuzzle.position).normalized;

        float maxAngle = Mathf.Lerp(25f, 0f, accuracy);
        dir = (Quaternion.Euler(
            Random.Range(-maxAngle, maxAngle),
            Random.Range(-maxAngle, maxAngle),
            0f) * dir).normalized;

        var b      = Instantiate(bulletPrefab, weaponMuzzle.position, Quaternion.LookRotation(dir));
        var bullet = b.GetComponent<Bullet>();
        if (bullet != null)
        {
            bullet.baseDamage = shootDamage;
            bullet.Initialize(dir, fromEnemy: true, owner: gameObject);
        }
    }

    private void ShootAtPosition(Vector3 targetWorldPos)
    {
        if (weaponMuzzle == null) return;

        _anim?.SetTrigger("Shoot");

        Vector3 dir      = (targetWorldPos - weaponMuzzle.position).normalized;
        float maxAngle   = Mathf.Lerp(25f, 5f, accuracy);
        dir = (Quaternion.Euler(
            Random.Range(-maxAngle, maxAngle),
            Random.Range(-maxAngle, maxAngle),
            0f) * dir).normalized;

        if (bulletPrefab != null)
        {
            var b      = Instantiate(bulletPrefab, weaponMuzzle.position, Quaternion.LookRotation(dir));
            var bullet = b.GetComponent<Bullet>();
            if (bullet != null)
            {
                bullet.baseDamage = shootDamage * 0.7f;
                bullet.Initialize(dir, fromEnemy: true, owner: gameObject);
            }
        }
        else
        {
            // Hitscan suppressive — uses shootMask to avoid invisible walls
            if (Physics.SphereCast(weaponMuzzle.position, 0.12f, dir,
                out RaycastHit hit, shootingRange * 1.5f, shootMask))
            {
                hit.collider.GetComponentInParent<IDamageable>()
                   ?.TakeDamage(shootDamage * 0.7f, gameObject);
            }
        }

        _weaponSFX?.PlayShot();
        NoiseEmitter.EmitNoise(weaponMuzzle.position, 35f, NoiseType.Gunshot, gameObject);
    }

    private void ShootHitscan()
    {
        float yDiff = Mathf.Abs(_player.transform.position.y - weaponMuzzle.position.y);
        if (yDiff > maxFloorHeightDiff) return;

        Vector3 targetPos = _player.transform.position + Vector3.up * 0.9f;
        Vector3 dir       = (targetPos - weaponMuzzle.position).normalized;
        dir += new Vector3(Random.Range(-0.08f, 0.08f),
                           Random.Range(-0.05f, 0.05f),
                           Random.Range(-0.08f, 0.08f));

        // shootMask excludes NavMesh collider and trigger layers — no invisible walls
        if (Physics.SphereCast(weaponMuzzle.position, 0.12f, dir.normalized,
            out RaycastHit hit, shootingRange * 1.5f, shootMask))
        {
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null)
                dmg.TakeDamage(shootDamage, gameObject);
            else
                hit.collider.GetComponentInParent<PlayerHealth>()
                   ?.TakeDamage(shootDamage, gameObject);
        }
    }

    // ═══ Melee ═══════════════════════════════════════════════════

    private IEnumerator DelayedMeleeDamage()
    {
        yield return new WaitForSeconds(0.28f);
        if (_player == null) yield break;
        if (Dist(_player.transform.position) <= meleeRange + 0.4f)
        {
            var ph = _player.GetComponent<PlayerHealth>();
            if (ph != null) ph.TakeDamage(meleeDamage);
            else _player.GetComponentInParent<IDamageable>()?.TakeDamage(meleeDamage, gameObject);
        }
    }

    public void AnimEvent_MeleeDamage()
    {
        if (_player == null) return;
        if (Dist(_player.transform.position) > meleeRange + 0.4f) return;
        var ph = _player.GetComponent<PlayerHealth>();
        if (ph != null) ph.TakeDamage(meleeDamage);
        else _player.GetComponentInParent<IDamageable>()?.TakeDamage(meleeDamage, gameObject);
    }

    // ═══ Cleanup & Gizmos ════════════════════════════════════════

    private void OnDestroy()
    {
        NoiseEmitter.OnNoiseEmitted -= OnNoiseHeard;

        if (_contributingToMusic)
        {
            _globalEnemiesInCombat = Mathf.Max(0, _globalEnemiesInCombat - 1);
            if (_globalEnemiesInCombat <= 0)
                AudioManager.Instance?.PlayMusic(AudioManager.MusicState.Main);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 eye  = transform.position + Vector3.up * 1.6f;
        float   half = visionAngle * 0.5f;

        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(eye, Quaternion.Euler(0, -half, 0) * transform.forward * visionRange);
        Gizmos.DrawRay(eye, Quaternion.Euler(0,  half, 0) * transform.forward * visionRange);
        Gizmos.DrawRay(eye, transform.forward * visionRange);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, peripheralRange);

        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, meleeRange);
    }
}
