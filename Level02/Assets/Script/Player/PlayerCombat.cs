using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;
// ─────────────────────────────────────────────────────────────

[RequireComponent(typeof(PlayerHealth))]
public class PlayerCombat : MonoBehaviour
{
    // ── Cameras ───────────────────────────────────────────────
    [Header("Cinemachine Cameras")]

    // v2: CinemachineVirtualCamera
    // v3: swap to CinemachineCamera  (only these two lines change for v3)
    [Tooltip("Your existing Starter Assets follow camera")]
    public CinemachineCamera normalCam;

    // aimCam no longer needed — CameraAimController handles blending on one camera.
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
    [Tooltip("Dot crosshair shown during hip-fire (active by default)")]
    public GameObject hipCrosshair;

    [Tooltip("Tighter crosshair shown while aiming")]
    public GameObject adsCrosshair;

    [Tooltip("Brief X marker shown when a bullet connects with an IDamageable")]
    public GameObject hitMarker;

    // ── Audio ─────────────────────────────────────────────────
    [Header("Audio (optional)")]
    public AudioClip shootSFX;
    public AudioClip aimInSFX;
    public AudioClip aimOutSFX;

    // ── Runtime ───────────────────────────────────────────────
    [Header("Weapon State")]
    [Tooltip("If true, player keeps the weapon after death/respawn.")]
    public bool keepWeaponOnRespawn = false;

    [Header("Ammo")]
    [Tooltip("Current rounds in the magazine. Loaded by WeaponPickup via AddAmmo().")]
    private int currentAmmo = 0;

    public int  CurrentAmmo => currentAmmo;

    private bool         hasWeapon;

    private bool         isAiming;
    private float        nextFireTime;
    private float        recoilY;
    private Camera       mainCamera;
    private PlayerHealth health;
    private Animator     animator;
    private AudioSource  audioSource;
    private float        defaultMoveSpeed;    // cached once in Start from TPC
    private float        defaultSprintSpeed;  // cached once in Start from TPC

    private bool _hasShootTrigger;   // cached at Start — avoids crash if trigger missing

    // ─────────────────────────────────────────────────────────
    // INIT
    // ─────────────────────────────────────────────────────────
    void Awake()
    {
        health      = GetComponent<PlayerHealth>();
        animator    = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();
        mainCamera  = Camera.main;

        // Cache speeds FIRST — before SetAimMode is called, so it never
        // reads a zero-initialized value and sets TPC.MoveSpeed = 0.
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
        // Cache whether the player animator has a Shoot trigger.
        _hasShootTrigger = HasAnimatorParam("Shoot", AnimatorControllerParameterType.Trigger);

        // Hide crosshairs until weapon is picked up
        if (hipCrosshair != null) hipCrosshair.SetActive(false);
        if (adsCrosshair  != null) adsCrosshair.SetActive(false);
    }


    // ─────────────────────────────────────────────────────────
    // UPDATE
    // ─────────────────────────────────────────────────────────
    void Update()
    {
        if (health.IsDead) return;

        HandleAimInput();
        HandleShootInput();
        RecoverRecoil();
    }

    // ─────────────────────────────────────────────────────────
    // AIMING
    // ─────────────────────────────────────────────────────────
    void HandleAimInput()
    {
        if (!hasWeapon) { if (isAiming) SetAimMode(false); return; }
        // RMB = aim (hold) — New Input System
        bool wantsAim = Mouse.current != null && Mouse.current.rightButton.isPressed;

        if (wantsAim == isAiming) return;

        isAiming = wantsAim;
        SetAimMode(isAiming);
    }

    void SetAimMode(bool aim, bool instant = false)
    {
        // ── Delegate camera to CameraAimController ────────────
        // SOTTR-style smooth blend handled there.
        var camAim = GetComponent<CameraAimController>();
        if (camAim != null)
            camAim.SetAim(aim, instant);
        else
        {
            // Fallback: priority swap (old system)
            if (normalCam != null)
                normalCam.Priority = aim ? (normalCamPriority - 5) : normalCamPriority;
            if (aimCam != null)
                aimCam.Priority    = aim ? aimCamPriority : (aimCamPriority - 10);
        }

        SetCrosshair(aim);

        // ── Speed control while aiming ───────────────────────
        // Uses values cached in Start() — never reads back from TPC to avoid
        // the "half of half" drift bug.
        var tpc = GetComponent<StarterAssets.ThirdPersonController>();
        if (tpc != null && defaultMoveSpeed > 0f)  // guard: never apply if speeds not cached yet
        {
            if (aim)
            {
                float aimSpeed     = defaultMoveSpeed * aimMoveSpeedMultiplier;
                tpc.MoveSpeed      = aimSpeed;
                tpc.SprintSpeed    = aimSpeed;
            }
            else
            {
                tpc.MoveSpeed      = defaultMoveSpeed;
                tpc.SprintSpeed    = defaultSprintSpeed;
            }
        }

        // ── SFX ──────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────
    // SHOOTING
    // ─────────────────────────────────────────────────────────
    void HandleShootInput()
    {
        if (!hasWeapon) return;
        if (currentAmmo <= 0) return;  // no rounds — block fire
        // LMB = shoot (hold = auto, Down = semi-auto) — New Input System
        bool wantsShoot = Mouse.current != null && Mouse.current.leftButton.isPressed;

        if (!wantsShoot) return;
        if (Time.time < nextFireTime) return;

        Fire();
    }

    void Fire()
    {
        if (currentAmmo <= 0) return;  // double-guard
        currentAmmo--;
        OnAmmoChanged?.Invoke(currentAmmo);  // notify HUD
        nextFireTime = Time.time + fireRate;

        // ── Uncharted mechanic: aim ray from SCREEN CENTER ────
        // This gives a world target point. The bullet physically travels
        // from the muzzle TOWARD that point (not from the camera).
        Ray screenRay = mainCamera.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        Vector3 aimWorldTarget;
        bool hitSomething = Physics.Raycast(screenRay, out RaycastHit aimHit, 300f, aimLayer);

        aimWorldTarget = hitSomething ? aimHit.point : screenRay.GetPoint(200f);

        // Hit marker when crosshair is over an IDamageable
        if (hitSomething && aimHit.collider != null)
        {
            IDamageable dmg = aimHit.collider.GetComponentInParent<IDamageable>();
            if (dmg != null && hitMarker != null)
                StartCoroutine(ShowHitMarker());
        }

        // ── Direction from muzzle to aim target ──────────────
        if (muzzlePoint == null)
        {
            Debug.LogWarning("[PlayerCombat] MuzzlePoint not assigned in Inspector.");
            return;
        }

        Vector3 dir = (aimWorldTarget - muzzlePoint.position).normalized;

        // ── Apply spread + recoil ─────────────────────────────
        float spread = isAiming ? adsSpread : hipFireSpread;
        dir = ApplySpread(dir, spread);

        // ── Spawn bullet ──────────────────────────────────────
        if (bulletPrefab != null)
        {
            GameObject bulletGO = Instantiate(
                bulletPrefab,
                muzzlePoint.position,
                Quaternion.LookRotation(dir));

            Bullet bullet = bulletGO.GetComponent<Bullet>();
            if (bullet != null)
                bullet.Initialize(dir, fromEnemy: false, owner: gameObject);
        }

        // ── Recoil accumulate ─────────────────────────────────
        recoilY += recoilPerShot;

        // ── Animator (only fires if 'Shoot' trigger exists in controller) ──
        if (animator != null && _hasShootTrigger)
            animator.SetTrigger("Shoot");

        // ── Audio ─────────────────────────────────────────────
        if (audioSource != null && shootSFX != null)
            audioSource.PlayOneShot(shootSFX);

        // ── Noise (alerts nearby EnemyAI via your NoiseEmitter) ──
        NoiseEmitter.EmitNoise(transform.position, 30f, NoiseType.Gunshot, gameObject);
    }

    // ─────────────────────────────────────────────────────────
    // HELPERS
    // ─────────────────────────────────────────────────────────
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

    System.Collections.IEnumerator ShowHitMarker()
    {
        if (hitMarker == null) yield break;
        hitMarker.SetActive(true);
        yield return new WaitForSeconds(0.08f);
        hitMarker.SetActive(false);
    }

    // ── Read-only property for other systems ─────────────────
    public bool IsAiming => isAiming;

    // ── Ammo event (subscribe from HUD to update ammo counter) ──
    public event System.Action<int> OnAmmoChanged;

    // ── Weapon equip / unequip ───────────────────────────────
    /// <summary>
    /// Called by RiflePickup when the player collects the weapon.
    /// Enables shooting and aiming.
    /// </summary>
    public void EquipWeapon()
    {
        hasWeapon = true;
        if (hipCrosshair != null) hipCrosshair.SetActive(true);
        Debug.Log("[PlayerCombat] Weapon equipped.");
    }

    public void UnequipWeapon()
    {
        hasWeapon = false;
        ClearAmmo();
        SetAimMode(false);
        SetCrosshair(false);
        if (hipCrosshair != null) hipCrosshair.SetActive(false);
        if (adsCrosshair  != null) adsCrosshair.SetActive(false);
    }

    public bool HasWeapon => hasWeapon;

    /// <summary>
    /// Add rounds to the player's current count.
    /// Called by WeaponPickup alongside PlayerInventory.CollectWeaponOrAmmo().
    /// </summary>
    public void AddAmmo(int amount)
    {
        currentAmmo += amount;
        OnAmmoChanged?.Invoke(currentAmmo);
        Debug.Log($"[PlayerCombat] Ammo +{amount} → {currentAmmo} rounds");
    }

    /// <summary>Empty ammo on unequip/respawn (if keepWeaponOnRespawn = false).</summary>
    public void ClearAmmo() { currentAmmo = 0; OnAmmoChanged?.Invoke(0); }

    // ── Animator parameter guard ──────────────────────────────
    /// <summary>Returns true if the animator has a parameter with the given name and type.</summary>
    bool HasAnimatorParam(string paramName, AnimatorControllerParameterType type)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters)
            if (p.name == paramName && p.type == type) return true;
        return false;
    }
}