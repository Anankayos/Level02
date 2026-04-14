using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PlayerInventory : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────
    public static PlayerInventory Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    // ── Weapon References (assign in Inspector) ────────────────
    [Header("Weapon")]
    [SerializeField] private GameObject riflePrefab;      // The AR model shown in player hands
    [SerializeField] private Transform  weaponHoldPoint;  // Empty child GameObject at hand/camera position

    // ── Keys ──────────────────────────────────────────────────
    private readonly HashSet<KeyType> _keys = new();
    public UnityEvent<KeyType> OnKeyCollected;

    // ── Weapon / Ammo ─────────────────────────────────────────
    private bool       _hasRifle;
    private int        _ammo;
    private GameObject _equippedRifle;
    public UnityEvent<bool, int> OnWeaponStateChanged; // (hasRifle, ammoCount)

    // ── ATM Cards ─────────────────────────────────────────────
    private int _atmCards;
    public UnityEvent<int> OnATMCardCollected;

    // ── Intel Files ───────────────────────────────────────────
    private readonly List<IntelData> _intel = new();
    public UnityEvent<IntelData> OnIntelCollected;

    // ═══════════════════════════════════════════════════════════
    //  PUBLIC API
    // ═══════════════════════════════════════════════════════════

    public void CollectKey(KeyType type)
    {
        _keys.Add(type);
        Debug.Log($"[Inventory] Key collected: {type}");
        OnKeyCollected?.Invoke(type);
    }

    public bool HasKey(KeyType type) => _keys.Contains(type);

    // ── Weapon: first pickup = equip rifle, rest = add ammo ───
    public void CollectWeaponOrAmmo(int ammoAmount)
    {
        if (!_hasRifle)
        {
            _hasRifle = true;
            _ammo = ammoAmount;

            if (riflePrefab != null && weaponHoldPoint != null)
            {
                _equippedRifle = Instantiate(
                    riflePrefab,
                    weaponHoldPoint.position,
                    weaponHoldPoint.rotation,
                    weaponHoldPoint   // parent to hand so it follows player
                );
            }

            Debug.Log($"[Inventory] Assault Rifle equipped! Ammo: {_ammo}");
        }
        else
        {
            _ammo += ammoAmount;
            Debug.Log($"[Inventory] Ammo +{ammoAmount} → Total: {_ammo}");
        }

        OnWeaponStateChanged?.Invoke(_hasRifle, _ammo);
    }

    public void CollectATMCard()
    {
        _atmCards++;
        Debug.Log($"[Inventory] ATM Card #{_atmCards} collected");
        OnATMCardCollected?.Invoke(_atmCards);
    }

    public void CollectIntel(IntelData data)
    {
        _intel.Add(data);
        Debug.Log($"[Inventory] Intel: \"{data.title}\" collected");
        OnIntelCollected?.Invoke(data);
    }

    public void UseAmmo(int amount)
    {
        _ammo = Mathf.Max(0, _ammo - amount);
        OnWeaponStateChanged?.Invoke(_hasRifle, _ammo);
    }
    public void RestoreFromCheckpoint(CheckpointData data)
    {
        _keys.Clear();
        _atmCards = 0;
        _intel.Clear();

        if (_equippedRifle != null) { Destroy(_equippedRifle); _equippedRifle = null; }
        _hasRifle = false;
        _ammo     = 0;

        foreach (var key in data.collectedKeys) _keys.Add(key);
        _atmCards = data.atmCards;
        _intel.AddRange(data.collectedIntel);

        if (data.hasRifle)
        {
            _hasRifle = true;
            _ammo     = data.ammo;
            if (riflePrefab != null && weaponHoldPoint != null)
                _equippedRifle = Instantiate(riflePrefab, weaponHoldPoint.position,
                                            weaponHoldPoint.rotation, weaponHoldPoint);
        }
        OnWeaponStateChanged?.Invoke(_hasRifle, _ammo);
    }

    // Getters for UI / other systems
    public bool         HasRifle    => _hasRifle;
    public int          Ammo        => _ammo;
    public int          ATMCards    => _atmCards;
    public List<IntelData> Intel    => _intel;
}