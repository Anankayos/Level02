using UnityEngine;
using Unity.Cinemachine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerCombat : MonoBehaviour, IResettable
{
    [Header("Cinemachine Cameras")]
    public CinemachineCamera normalCam;
    [HideInInspector] public CinemachineCamera aimCam;

    [Header("Weapon")]
    public Transform muzzlePoint;
    public GameObject bulletPrefab;
    public LayerMask aimLayer = ~0;

    [Header("Shooting")]
    public float fireRate = 0.25f;
    public float hipFireSpread = 5f;
    public float adsSpread = 0.8f;
    public float minHitDistance = 1.5f;

    [Header("Shooting - Floor Fix")]
    public LayerMask bulletHitMask;
    public float aimFallbackDistance = 300f;

    [Header("Recoil")]
    public float recoilPerShot = 0.8f;
    public float recoilRecoverySpeed = 4f;

    [Header("Camera Priorities")]
    public int normalCamPriority = 10;
    public int aimCamPriority = 15;

    [Header("Aim Movement")]
    public float aimMoveSpeedMultiplier = 0.45f;

    [Header("HUD References")]
    public GameObject hipCrosshair;
    public GameObject adsCrosshair;
    public HitMarkerController hitMarker;

    [Header("Audio (Aiming Only)")]
    public AudioClip aimInSFX;
    public AudioClip aimOutSFX;

    [Header("Weapon State")]
    public bool keepWeaponOnRespawn = false;

    private int currentAmmo = 0;
    public int CurrentAmmo => currentAmmo;
    public int ReserveAmmo => 0;

    public string ResettableID => gameObject.GetInstanceID().ToString();
    public void SaveInitialState() { }

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
    private WeaponSFX        _weaponSFX; // <-- ADDED THIS
    private float            defaultMoveSpeed;
    private float            defaultSprintSpeed;
    private CrosshairController _crosshair;

    public event System.Action<int> OnAmmoChanged;
    public bool IsAiming  => isAiming;
    public bool HasWeapon => hasWeapon;

    void Awake()
    {
        health      = GetComponent<PlayerHealth>();
        animator    = GetComponent<Animator>();
        audioSource = GetComponent<AudioSource>();
        _weaponSFX  = GetComponent<WeaponSFX>(); // <-- ADDED THIS
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

    private void OnEnable() => isAiming = false;

    private System.Collections.IEnumerator BroadcastInitialState()
    {
        yield return null;
        if (hasWeapon)
        {
            GameEvents.FireWeaponEquipped("Rifle");
            GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);
        }
    }

    void Update()
    {
        if (health.IsDead) return;
        HandleAimInput();
        HandleShootInput();
        RecoverRecoil();
    }

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
        if (camAim != null) camAim.SetAim(aim, instant);
        else
        {
            if (normalCam != null) normalCam.Priority = aim ? (normalCamPriority - 5) : normalCamPriority;
            if (aimCam != null)    aimCam.Priority    = aim ? aimCamPriority : (aimCamPriority - 10);
        }

        SetCrosshair(aim);

        var tpc = GetComponent<StarterAssets.ThirdPersonController>();
        if (tpc != null && defaultMoveSpeed > 0f)
        {
            float targetSpeed = aim ? (defaultMoveSpeed * aimMoveSpeedMultiplier) : defaultMoveSpeed;
            tpc.MoveSpeed = targetSpeed;
            tpc.SprintSpeed = aim ? targetSpeed : defaultSprintSpeed;
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

    void HandleShootInput()
    {
        if (!hasWeapon || currentAmmo <= 0) return;

        bool wantsShoot = Mouse.current != null && Mouse.current.leftButton.isPressed;
        if (!wantsShoot || Time.time < nextFireTime) return;

        Fire();
    }

    void Fire()
    {
        if (currentAmmo <= 0) return;
        currentAmmo--;
        nextFireTime = Time.time + fireRate;

        OnAmmoChanged?.Invoke(currentAmmo);
        GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);

        if (muzzlePoint == null) return;

        Ray screenRay = mainCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        Vector3 aimWorldTarget;

        if (Physics.Raycast(screenRay, out RaycastHit enemyHit, aimFallbackDistance, bulletHitMask))
            aimWorldTarget = enemyHit.point;
        else if (Physics.Raycast(screenRay, out RaycastHit geoHit, aimFallbackDistance, aimLayer) && geoHit.distance > 2f)
            aimWorldTarget = geoHit.point;
        else
            aimWorldTarget = screenRay.GetPoint(aimFallbackDistance);

        Vector3 dir = (aimWorldTarget - muzzlePoint.position).normalized;
        dir = ApplySpread(dir, isAiming ? adsSpread : hipFireSpread);

        if (bulletPrefab != null)
        {
            GameObject bulletGO = Instantiate(bulletPrefab, muzzlePoint.position, Quaternion.LookRotation(dir));
            Bullet bullet = bulletGO.GetComponent<Bullet>();
            if (bullet != null) bullet.Initialize(dir, fromEnemy: false, owner: gameObject);
        }

        recoilY += recoilPerShot;
        _crosshair?.OnShot();

        if (animator != null && _hasShootTrigger) animator.SetTrigger("Shoot");

        _weaponSFX?.PlayShot(); // <-- SOUND PLAYS DIRECTLY FROM PLAYER HERE
        NoiseEmitter.EmitNoise(transform.position, 30f, NoiseType.Gunshot, gameObject);
    }

    public void EquipWeapon()
    {
        hasWeapon = true;
        if (hipCrosshair != null) hipCrosshair.SetActive(true);
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

    public void ResetState()
    {
        isAiming = false;
        SetAimMode(false, instant: true);
        if (!keepWeaponOnRespawn) { UnequipWeapon(); return; }
        GameEvents.FireWeaponEquipped("Rifle");
        GameEvents.FireAmmoChanged(currentAmmo, ReserveAmmo);
    }

    public void ForceResetAim()
    {
        isAiming = false;
        _aimInputCooldown = 0.25f;
        SetAimMode(false, instant: true);
        var tpc = GetComponent<StarterAssets.ThirdPersonController>();
        if (tpc != null && defaultMoveSpeed > 0f)
        {
            tpc.MoveSpeed = defaultMoveSpeed;
            tpc.SprintSpeed = defaultSprintSpeed;
        }
        SetCrosshair(false);
    }

    Vector3 ApplySpread(Vector3 direction, float spreadDeg)
    {
        float rx = Random.Range(-spreadDeg, spreadDeg) + recoilY;
        float ry = Random.Range(-spreadDeg, spreadDeg);
        return (Quaternion.Euler(rx, ry, 0f) * direction).normalized;
    }

    void RecoverRecoil() => recoilY = Mathf.Lerp(recoilY, 0f, recoilRecoverySpeed * Time.deltaTime);

    bool HasAnimatorParam(string paramName, AnimatorControllerParameterType type)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters) if (p.name == paramName && p.type == type) return true;
        return false;
    }
}