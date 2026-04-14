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
    [SerializeField] private float      visionRange      = 15f;
    [SerializeField, Range(10, 180)]
                     private float      visionAngle      = 65f;
    [SerializeField] private float      alertVisionAngle = 130f;
    [SerializeField] private float      peripheralRange  = 3f;
    [SerializeField] private LayerMask  obstacleLayer;

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
    private State        _state;
    private bool         _ready;

    // Health
    private float _hp;

    // Last known player position
    private Vector3 _lkp;
    private bool    _hasLKP;

    // Patrol
    private int   _wpIndex;
    private float _wpTimer;
    private bool  _wpWaiting;
    private bool  _wpReady;      // defers first SetDestination to Tick

    // Suspicious
    private float _suspicion;
    private float _suspLookTimer;
    private bool  _suspMoving;

    // Search
    private float _searchTimer;

    // Combat
    private float        _nextShot;
    private float        _lostSight;
    private const float  LostSightTimeout = 3f;

    // Melee
    private float _nextMelee;

    // IResettable
    private string     _id;
    private Vector3    _initPos;
    private Quaternion _initRot;

    public string ResettableID =>
        string.IsNullOrEmpty(_id) ? (_id = System.Guid.NewGuid().ToString()) : _id;

    // ═══ Unity Messages ══════════════════════════════════════════

    private void Awake()
    {
        _agent  = GetComponent<NavMeshAgent>();
        _anim   = GetComponent<Animator>();
        _player = GameObject.FindWithTag("Player");
        _hp     = maxHealth;
        _initPos = transform.position;
        _initRot = transform.rotation;

        // Disable BEFORE Start so Unity never auto-places at a bad position
        _agent.enabled = false;

        NoiseEmitter.OnNoiseEmitted += OnNoiseHeard;
    }

    private IEnumerator Start()
    {
        // Move transform to closest NavMesh point while agent is still disabled
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 10f, NavMesh.AllAreas))
            transform.position = hit.position;

        yield return new WaitForEndOfFrame();

        // Enable — Unity now auto-places agent at transform.position
        _agent.enabled = true;

        yield return new WaitForEndOfFrame();

        if (!_agent.isOnNavMesh)
        {
            Debug.LogError($"[EnemyAI '{name}'] Not on NavMesh. " +
                           "Bake the NavMesh and ensure the enemy touches the blue surface.");
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
    }

    private void EnterState(State s)
    {
        switch (s)
        {
            case State.Patrol:
                _agent.speed = patrolSpeed;
                _wpWaiting   = false;
                _wpReady     = false;   // first destination set in Tick
                break;

            case State.Suspicious:
                _agent.speed   = patrolSpeed * 0.75f;
                _suspLookTimer = 3.5f;
                _suspMoving    = _hasLKP;
                _anim?.SetBool("IsAlert", true);
                if (_hasLKP) Move(_lkp);
                Debug.Log($"[{name}] ? Suspicious");
                break;

            case State.Search:
                _agent.speed = chaseSpeed * 0.7f;
                _searchTimer = searchDuration;
                _anim?.SetBool("IsAlert",     true);
                _anim?.SetBool("IsSearching", true);
                if (_hasLKP) Move(_lkp);
                Debug.Log($"[{name}] !! Searching");
                break;

            case State.Combat:
                _agent.speed = chaseSpeed;
                _lostSight   = 0f;
                _nextShot    = Time.time + 0.6f;
                _anim?.SetBool("IsInCombat", true);
                Debug.Log($"[{name}] !!! Combat");
                break;

            case State.Melee:
                _agent.speed = chaseSpeed;
                _nextMelee   = Time.time + 0.4f;
                _anim?.SetBool("IsMelee", true);
                Debug.Log($"[{name}] Melee");
                break;

            case State.Dead:
                _agent.ResetPath();
                _agent.enabled                   = false;
                GetComponent<Collider>().enabled = false;
                _anim?.SetTrigger("Die");
                _anim?.SetBool("IsDead", true);
                SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);
                Debug.Log($"[{name}] Dead");
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
                _agent.ResetPath();
                _anim?.SetBool("IsInCombat", false);
                _anim?.SetBool("IsMoving",   false);
                break;
            case State.Melee:
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
        // Defer first waypoint to Tick so agent is confirmed ready
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
        // Player outside zone → lose track quickly
        if (!PlayerInZone())
        {
            _lostSight += Time.deltaTime;
            if (_hasLKP) Move(_lkp);
            if (_lostSight >= 1.5f) { _hasLKP = false; SetState(State.Search); }
            return;
        }

        bool sees = CanSeePlayer();
        if (sees)
        {
            StampLKP();
            _lostSight = 0f;

            float dist = Dist(_player.transform.position);
            if (dist <= meleeRange) { SetState(State.Melee); return; }

            // Maintain engagement distance
            if      (dist > shootingRange * 0.75f) Move(_lkp);
            else if (dist < shootingRange * 0.4f)  Move(transform.position +
                         (transform.position - _player.transform.position).normalized * 4f);
            else                                    _agent.ResetPath();

            FaceTarget(_player.transform.position);

            if (Time.time >= _nextShot) { Shoot(); _nextShot = Time.time + shootCooldown; }
        }
        else
        {
            _lostSight += Time.deltaTime;
            if (_hasLKP) Move(_lkp);
            if (_lostSight >= LostSightTimeout) SetState(State.Search);
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
        Vector3 dir = (target + Vector3.up * 0.9f) - eye;
        return !Physics.Raycast(eye, dir.normalized, out _, dir.magnitude, obstacleLayer);
    }

    // ═══ Hearing ════════════════════════════════════════════════

    private void OnNoiseHeard(Vector3 pos, float radius, NoiseType type, GameObject source)
    {
        // Ignore own footsteps and any noise this enemy generated itself
        if (source == gameObject) return;

        if (_state == State.Dead || !_ready) return;

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

    public bool IsAlive => _hp > 0f;

    public void TakeDamage(float amount, GameObject source = null)
    {
        if (!IsAlive) return;
        _hp -= amount;
        _anim?.SetTrigger("Hit");

        if (source != null) { _lkp = source.transform.position; _hasLKP = true; }

        if (_hp <= 0f)                                           SetState(State.Dead);
        else if (_state is not (State.Combat or State.Melee))   SetState(State.Combat);
    }

    // ═══ IResettable ═════════════════════════════════════════════

    public void SaveInitialState() { }

    public void ResetState()
    {
        _hp        = maxHealth;
        _suspicion = 0f;
        _hasLKP    = false;
        _ready     = false;

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
            transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 12f
        );
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

    private void Shoot()
    {
        if (weaponMuzzle == null || _player == null) return;
        _anim?.SetTrigger("Shoot");

        Vector3 dir = (_player.transform.position + Vector3.up * 0.9f) - weaponMuzzle.position;
        dir += new Vector3(Random.Range(-0.08f, 0.08f),
                           Random.Range(-0.05f, 0.05f),
                           Random.Range(-0.08f, 0.08f));

        if (Physics.Raycast(weaponMuzzle.position, dir.normalized,
                            out RaycastHit hit, shootingRange * 1.5f))
            hit.collider.GetComponent<IDamageable>()?.TakeDamage(shootDamage, gameObject);
    }

    private IEnumerator DelayedMeleeDamage()
    {
        yield return new WaitForSeconds(0.28f);
        if (_player == null) yield break;
        if (Dist(_player.transform.position) <= meleeRange + 0.4f)
            _player.GetComponent<IDamageable>()?.TakeDamage(meleeDamage, gameObject);
    }

    // ═══ Gizmos ══════════════════════════════════════════════════

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

    private void OnDestroy() => NoiseEmitter.OnNoiseEmitted -= OnNoiseHeard;
}