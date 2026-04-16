using UnityEngine;
using UnityEngine.UI;
using TMPro;
using StarterAssets;

public class CrosshairController : MonoBehaviour
{
    // ── Lines ─────────────────────────────────────────────────
    [Header("Crosshair Lines")]
    [SerializeField] private RectTransform lineTop;
    [SerializeField] private RectTransform lineBottom;
    [SerializeField] private RectTransform lineLeft;
    [SerializeField] private RectTransform lineRight;
    [SerializeField] private Image         centerDot;

    // ── Ammo ──────────────────────────────────────────────────
    [Header("Ammo Display")]
    [SerializeField] private GameObject      ammoGroup;        // near crosshair (ADS)
    [SerializeField] private TextMeshProUGUI ammoMagText;
    [SerializeField] private TextMeshProUGUI ammoReserveText;
    [SerializeField] private GameObject      ammoCornerPanel;  // bottom-right (hip-fire)

    // ── Spread Settings ───────────────────────────────────────
    [Header("Spread")]
    [SerializeField] private float restGap     = 18f;
    [SerializeField] private float moveGap     = 34f;
    [SerializeField] private float shootGap    = 48f;
    [SerializeField] private float adsGap      = 8f;
    [SerializeField] private float spreadSpeed = 12f;

    // ── Colors ────────────────────────────────────────────────
    [Header("Colors")]
    [SerializeField] private Color colorNormal = new Color(1f, 1f, 1f, 0.90f);
    [SerializeField] private Color colorAds    = new Color(1f, 1f, 1f, 0.60f);
    [SerializeField] private Color colorEmpty  = new Color(1f, 0.25f, 0.15f, 0.95f);

    // ── Runtime ───────────────────────────────────────────────
    private float                 _currentGap;
    private float                 _targetGap;
    private float                 _shootSpike;
    private bool                  _wasAiming;
    private int                   _lastAmmo = -1;

    private PlayerCombat          _combat;
    private CharacterController   _cc;


    // ═══ Lifecycle ════════════════════════════════════════════

    private void Awake()
    {
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            _combat = player.GetComponentInChildren<PlayerCombat>();
            _cc     = player.GetComponentInChildren<CharacterController>();
        }

        _currentGap = restGap;
        _targetGap  = restGap;

        GameEvents.OnAmmoChanged      += HandleAmmoChanged;
        GameEvents.OnWeaponEquipped   += HandleWeaponEquipped;
        GameEvents.OnWeaponUnequipped += HandleWeaponUnequipped;
    }

    private void OnDestroy()
    {
        GameEvents.OnAmmoChanged      -= HandleAmmoChanged;
        GameEvents.OnWeaponEquipped   -= HandleWeaponEquipped;
        GameEvents.OnWeaponUnequipped -= HandleWeaponUnequipped;
    }

    private void OnEnable()
    {
        GameEvents.OnAmmoChanged      += HandleAmmoChanged;
        GameEvents.OnWeaponEquipped   += HandleWeaponEquipped;
        GameEvents.OnWeaponUnequipped += HandleWeaponUnequipped;
    }

    private void OnDisable()
    {
        GameEvents.OnAmmoChanged      -= HandleAmmoChanged;
        GameEvents.OnWeaponEquipped   -= HandleWeaponEquipped;
        GameEvents.OnWeaponUnequipped -= HandleWeaponUnequipped;
    }

    private void Start()
    {
        // Everything hidden until weapon is picked up
        gameObject.SetActive(false);
        if (ammoGroup       != null) ammoGroup.SetActive(false);
        if (ammoCornerPanel != null) ammoCornerPanel.SetActive(false);
    }

    private void Update()
    {
        if (_combat == null || !_combat.HasWeapon) return;

        CalculateTargetGap();
        AnimateLines();
        UpdateAmmoColor();
    }


    // ═══ Spread ═══════════════════════════════════════════════

    private void CalculateTargetGap()
    {
        bool isAiming = _combat != null && _combat.IsAiming;

        if (isAiming)
        {
            _targetGap = adsGap;
        }
        else
        {
            float speed     = _cc != null ? _cc.velocity.magnitude : 0f;
            float moveRatio = Mathf.Clamp01(speed / 5f);
            _targetGap = Mathf.Lerp(restGap, moveGap, moveRatio);
        }

        _shootSpike = Mathf.MoveTowards(_shootSpike, 0f, Time.deltaTime * spreadSpeed * 4f);
        _targetGap  = Mathf.Max(_targetGap, _shootSpike);
        _currentGap = Mathf.Lerp(_currentGap, _targetGap, Time.deltaTime * spreadSpeed);

        // Swap panels when aim state changes
        if (isAiming != _wasAiming)
        {
            _wasAiming = isAiming;
            SwapAmmoPanels(isAiming);
        }
    }

    private void SwapAmmoPanels(bool isAiming)
    {
        if (ammoGroup       != null) ammoGroup.SetActive(isAiming);
        if (ammoCornerPanel != null) ammoCornerPanel.SetActive(!isAiming && _combat.HasWeapon);
    }


    // ═══ Line Animation ═══════════════════════════════════════

    private void AnimateLines()
    {
        bool  ads = _combat != null && _combat.IsAiming;
        Color c   = ads ? colorAds : colorNormal;
        float g   = _currentGap;

        SetLine(lineTop,    0f,  g, c);
        SetLine(lineBottom, 0f, -g, c);
        SetLine(lineLeft,  -g, 0f, c);
        SetLine(lineRight,  g, 0f, c);

        if (centerDot != null)
        {
            centerDot.enabled = ads;
            centerDot.color   = c;
        }
    }

    private void SetLine(RectTransform rt, float x, float y, Color c)
    {
        if (rt == null) return;
        rt.anchoredPosition = new Vector2(x, y);
        var img = rt.GetComponent<Image>();
        if (img != null) img.color = c;
    }


    // ═══ Public: called by PlayerCombat.Fire() ════════════════

    public void OnShot()
    {
        _shootSpike = shootGap;
    }


    // ═══ Ammo Events ══════════════════════════════════════════

    private void HandleAmmoChanged(int mag, int reserve)
    {
        if (ammoMagText     != null) ammoMagText.text     = mag.ToString();
        if (ammoReserveText != null) ammoReserveText.text = reserve > 0
                                                            ? reserve.ToString() : "—";
        _lastAmmo = mag;
    }

    private void HandleWeaponEquipped(string weaponName)
    {
        gameObject.SetActive(true);
        if (ammoGroup       != null) ammoGroup.SetActive(false);
        if (ammoCornerPanel != null) ammoCornerPanel.SetActive(true);
        _wasAiming = false;
    }

    private void HandleWeaponUnequipped()
    {
        gameObject.SetActive(false);
        if (ammoGroup       != null) ammoGroup.SetActive(false);
        if (ammoCornerPanel != null) ammoCornerPanel.SetActive(false);
    }

    private void UpdateAmmoColor()
    {
        if (ammoMagText == null) return;
        ammoMagText.color = _lastAmmo == 0 ? colorEmpty : colorNormal;
    }
}