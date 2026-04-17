using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerCombat : MonoBehaviour, IResettable
{
    // ── Cameras ───────────────────────────────────────────────
    [Header("Cinemachine Cameras")]
    [Tooltip("Your existing Starter Assets follow camera")]
    public CinemachineCamera normalCam;

    [HideInInspector] public CinemachineCamera aimCam;

    // ── Weapon ────────────────────────────────────────────────
    [Header("Weapon")]
    [Tooltip("Empty child Transform at the gun barrel tip")]
    public Transform muzzlePoint;

    [Tooltip("Prefab with Bullet.cs")]
    public GameObject bulletPrefab;

    [Tooltip("Layers the aim ray can hit (enemies, world geometry)")]
    public LayerMask aimLayer = ~0;

   // ── Shooting ──────────────────────────────────────────────
    [Header("Shooting")]
    [Tooltip("Seconds between shots (0.25 = 4 shots/sec semi-auto feel)")]
    public float fireRate = 0.25f;

    [Tooltip("Hip-fire spread in degrees (±). Wider = less accurate")]
    public float hipFireSpread = 5f;

    [Tooltip("ADS spread in degrees (±). Tight when aiming")]
    public float adsSpread = 0.8f;

    [Tooltip("Ignore raycast hits closer than this distance (prevents hitting merged floor slabs)")]
    public float minHitDistance = 1.5f;
    [Header("Shooting - Floor Fix")]
    [Tooltip("Layer mask for what bullets can HIT. Assign Enemies + Destructibles only.")]
    public LayerMask bulletHitMask;

    [Tooltip("Fallback aim distance when no valid target is found.")]
    public float aimFallbackDistance = 300f;

    // ── Recoil ────────────────────────────────────────────────
    [Header("Recoil")]
    [Tooltip("Vertical spread added per shot")]
    public float recoilPerShot = 0.8f;

    [Tooltip("How fast recoil recovers to zero per second")]
    public float recoilRecoverySpeed = 4f;

    // ── Camera Priorities ─────────────────────────────────────
    [Header("Camera Priorities")]
    [Tooltip("Priority of the normal camera when NOT aiming")]
    public int normalCamPriority = 10;

    [Tooltip("Priority of the aim camera when aiming (must be > normalCamPriority)")]
    public int aimCamPriority = 15;

    // ── Movement while aiming ─────────────────────────────────
    [Header("Aim Movement")]
    [Tooltip("Movement speed multiplier while aiming (0.45 = 45% of normal speed)")]
    public float aimMoveSpeedMultiplier = 0.45f;

    // ── HUD ───────────────────────────────────────────────────
    [Header("HUD References")]
    [Tooltip("Dot crosshair shown during hip-fire — leave None if using CrosshairController")]
    public GameObject hipCrosshair;

    [Tooltip("Tighter crosshair shown while aiming — leave None if using CrosshairController")]
    public GameObject adsCrosshair;

    [Tooltip("HitMarker GameObject with HitMarkerController.cs")]
    public HitMarkerController hitMarker;

    // ── Audio ─────────────────────────────────────────────────
    [Header("Audio (optional)")]
    public AudioClip shootSFX;
    public AudioClip aimInSFX;
    public AudioClip aimOutSFX;

    // ── Weapon State ──────────────────────────────────────────
    [Header("Weapon State")]
    [Tooltip("If true, player keeps the weapon after death/respawn.")]
    public bool keepWeaponOnRespawn = false;

    // ── Ammo ──────────────────────────────────────────────────
    [Header("Ammo")]
    [Tooltip("Current rounds in the magazine. Loaded by WeaponPickup via AddAmmo().")]
    private int currentAmmo = 0;

    public int CurrentAmmo => currentAmmo;
    public int ReserveAmmo => 0;

    // ── IResettable ───────────────────────────────────────────
    public string ResettableID => gameObject.GetInstanceID().ToString();
    public void SaveInitialState() { }

    // ── Private Runtime ───────────────────────────────────────
    private bool        hasWeapon;
    private bool        isAiming;
    private float       nextFireTime;
    private float       recoilY;
    private float       _aimInputCooldown;
    private bool        _hasShootTrigger;

    private Camera           mainCamera;
    private PlayerHealth     health;
    private Animator         animator;
    private AudioSource      audioSource;
    private float            defaultMoveSpeed;
    private float            defaultSprintSpeed;
    private CrosshairController _crosshair;

    // ── Public Events ─────────────────────────────────────────
    public event System.Action<int> OnAmmoChanged;
    public bool IsAiming  => isAiming;
    public bool HasWeapon => hasWeapon;


    // ═════════════════════════════════════════════════════════
    // INIT
    // ═════════════════════════════════════════════════════════

    void Awake()
    {
        health      = GetComponent<PlayerHealth>();
        animator    = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();
        mainCamera  = Camera.main;
        _crosshair = Object.FindFirstObjectByType<CrosshairController>();

        var tpc = GetComponent<StarterAssets.ThirdPersonController>();
        if (tpc != null)
        {
            defaultMoveSpeed   = tpc.MoveSpeed;
            defaultSprintSpeed = tpc.SprintSpeed;
        }

        SetAimMode(false, instant: true);
    }

    void Start()
    {
        _hasShootTrigger = HasAnimatorParam("Shoot", AnimatorControllerParameterType.Trigger);

        if (hipCrosshair != null) hipCrosshair.SetActive(false);
        if (adsCrosshair != null) adsCrosshair.SetActive(false);

        StartCoroutine(BroadcastInitialState());
    }

    private void OnEnable()
    {
        // Reset aim flag — SetAimMode is called explicitly by ForceResetAim()
        isAiming = false;
    }

    private System.Collections.IEnumerator BroadcastInitialState()
    {
        yield return null; // wait one frame so CrosshairController.Awake() runs first
        if (hasWeapon)
        {
            GameEvents.FireWeaponEquipped("Rifle");
            GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);
        }
    }


    // ═════════════════════════════════════════════════════════
    // UPDATE
    // ═════════════════════════════════════════════════════════

    void Update()
    {
        if (health.IsDead) return;
        HandleAimInput();
        HandleShootInput();
        RecoverRecoil();
    }


    // ═════════════════════════════════════════════════════════
    // AIMING
    // ═════════════════════════════════════════════════════════

    void HandleAimInput()
    {
        if (_aimInputCooldown > 0f)
        {
            _aimInputCooldown -= Time.deltaTime;
            return;
        }

        if (!hasWeapon) { if (isAiming) SetAimMode(false); return; }

        bool wantsAim = Mouse.current != null && Mouse.current.rightButton.isPressed;
        if (wantsAim == isAiming) return;

        isAiming = wantsAim;
        SetAimMode(isAiming);
    }

    void SetAimMode(bool aim, bool instant = false)
    {
        var camAim = GetComponent<CameraAimController>();
        if (camAim != null)
        {
            camAim.SetAim(aim, instant);
        }
        else
        {
            if (normalCam != null)
                normalCam.Priority = aim ? (normalCamPriority - 5) : normalCamPriority;
            if (aimCam != null)
                aimCam.Priority    = aim ? aimCamPriority : (aimCamPriority - 10);
        }

        SetCrosshair(aim);

        var tpc = GetComponent<StarterAssets.ThirdPersonController>();
        if (tpc != null && defaultMoveSpeed > 0f)
        {
            if (aim)
            {
                float aimSpeed  = defaultMoveSpeed * aimMoveSpeedMultiplier;
                tpc.MoveSpeed   = aimSpeed;
                tpc.SprintSpeed = aimSpeed;
            }
            else
            {
                tpc.MoveSpeed   = defaultMoveSpeed;
                tpc.SprintSpeed = defaultSprintSpeed;
            }
        }

        if (!instant && audioSource != null)
        {
            AudioClip clip = aim ? aimInSFX : aimOutSFX;
            if (clip != null) audioSource.PlayOneShot(clip);
        }
    }

    void SetCrosshair(bool aiming)
    {
        if (hipCrosshair != null) hipCrosshair.SetActive(!aiming);
        if (adsCrosshair != null) adsCrosshair.SetActive(aiming);
    }


    // ═════════════════════════════════════════════════════════
    // SHOOTING
    // ═════════════════════════════════════════════════════════

    void HandleShootInput()
    {
        if (!hasWeapon)       return;
        if (currentAmmo <= 0) return;

        bool wantsShoot = Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (!wantsShoot)             return;
        if (Time.time < nextFireTime) return;

        Fire();
    }

 void Fire()
{
    if (currentAmmo <= 0) return;
    currentAmmo--;
    nextFireTime = Time.time + fireRate;

    OnAmmoChanged?.Invoke(currentAmmo);
    GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);

    if (muzzlePoint == null)
    {
        Debug.LogWarning("[PlayerCombat] MuzzlePoint not assigned.");
        return;
    }

    // ── Step 1: find aim target ignoring ALL geometry ──────────
    // Cast only against enemies/destructibles first
    Ray screenRay = mainCamera.ScreenPointToRay(
        new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

    Vector3 aimWorldTarget;

    if (Physics.Raycast(screenRay, out RaycastHit enemyHit, aimFallbackDistance, bulletHitMask))
    {
        // Crosshair is pointing at an enemy or destructible → aim at it
        aimWorldTarget = enemyHit.point;
    }
    else
    {
        // No enemy in crosshair → aim at a far point along the ray
        // Use aimLayer (full geometry) but with a minimum distance guard
        if (Physics.Raycast(screenRay, out RaycastHit geoHit, aimFallbackDistance, aimLayer)
            && geoHit.distance > 2f)
        {
            aimWorldTarget = geoHit.point;
        }
        else
        {
            aimWorldTarget = screenRay.GetPoint(aimFallbackDistance);
        }
    }

    // ── Step 2: fire bullet from muzzle TOWARD that world point ─
    Vector3 dir = (aimWorldTarget - muzzlePoint.position).normalized;
    dir = ApplySpread(dir, isAiming ? adsSpread : hipFireSpread);

    if (bulletPrefab != null)
    {
        GameObject bulletGO = Instantiate(bulletPrefab,
            muzzlePoint.position, Quaternion.LookRotation(dir));

        Bullet bullet = bulletGO.GetComponent<Bullet>();
        if (bullet != null)
            bullet.Initialize(dir, fromEnemy: false, owner: gameObject);
    }

    recoilY += recoilPerShot;
    _crosshair?.OnShot();

    if (animator != null && _hasShootTrigger)
        animator.SetTrigger("Shoot");

    if (audioSource != null && shootSFX != null)
        audioSource.PlayOneShot(shootSFX);

    NoiseEmitter.EmitNoise(transform.position, 30f, NoiseType.Gunshot, gameObject);
}


    // ═════════════════════════════════════════════════════════
    // WEAPON EQUIP / UNEQUIP
    // ═════════════════════════════════════════════════════════

    public void EquipWeapon()
    {
        hasWeapon = true;
        if (hipCrosshair != null) hipCrosshair.SetActive(true);
        Debug.Log("[PlayerCombat] Weapon equipped.");

        GameEvents.FireWeaponEquipped("Rifle");
        GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);
    }

    public void UnequipWeapon()
    {
        hasWeapon = false;
        ClearAmmo();
        SetAimMode(false);
        SetCrosshair(false);
        if (hipCrosshair != null) hipCrosshair.SetActive(false);
        if (adsCrosshair != null) adsCrosshair.SetActive(false);

        GameEvents.FireWeaponUnequipped();
    }

    public void AddAmmo(int amount)
    {
        currentAmmo += amount;
        OnAmmoChanged?.Invoke(currentAmmo);
        GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);
        Debug.Log($"[PlayerCombat] Ammo +{amount} → {currentAmmo} rounds");
    }

    public void ClearAmmo()
    {
        currentAmmo = 0;
        OnAmmoChanged?.Invoke(0);
        GameEvents.FireAmmoChanged(0, ReserveAmmo);
    }

    public void RestoreAmmo(int savedAmmo)
    {
        currentAmmo = savedAmmo;
        OnAmmoChanged?.Invoke(currentAmmo);
        GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);
    }


    // ═════════════════════════════════════════════════════════
    // IRESETTABLE
    // ═════════════════════════════════════════════════════════

    public void ResetState()
    {
        isAiming = false;
        SetAimMode(false, instant: true);

        if (!keepWeaponOnRespawn)
        {
            UnequipWeapon();
            return;
        }

        GameEvents.FireWeaponEquipped("Rifle");
        GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);
    }

    public void ForceResetAim()
    {
        isAiming          = false;
        _aimInputCooldown = 0.25f;
        SetAimMode(false, instant: true);

        var tpc = GetComponent<StarterAssets.ThirdPersonController>();
        if (tpc != null && defaultMoveSpeed > 0f)
        {
            tpc.MoveSpeed   = defaultMoveSpeed;
            tpc.SprintSpeed = defaultSprintSpeed;
        }

        SetCrosshair(false);
    }


    // ═════════════════════════════════════════════════════════
    // HELPERS
    // ═════════════════════════════════════════════════════════

    Vector3 ApplySpread(Vector3 direction, float spreadDeg)
    {
        float rx = Random.Range(-spreadDeg, spreadDeg) + recoilY;
        float ry = Random.Range(-spreadDeg, spreadDeg);
        return (Quaternion.Euler(rx, ry, 0f) * direction).normalized;
    }

    void RecoverRecoil()
    {
        recoilY = Mathf.Lerp(recoilY, 0f, recoilRecoverySpeed * Time.deltaTime);
    }

    bool HasAnimatorParam(string paramName, AnimatorControllerParameterType type)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters)
            if (p.name == paramName && p.type == type) return true;
        return false;
    }
}