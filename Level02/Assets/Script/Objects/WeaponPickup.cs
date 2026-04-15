using UnityEngine;

public class WeaponPickup : FloatingPickup
{
    [Header("Ammo")]
    [SerializeField] private int initialAmmo = 30;
    [SerializeField] private int bonusAmmo   = 30;

    [Header("Hand Attachment")]
    [Tooltip("Exact name of the right-hand bone in the player's rig. " +
             "Starter Assets / Mixamo: 'mixamorig:RightHand'")]
    [SerializeField] private string handBoneName = "mixamorig:RightHand";

    [Tooltip("The weapon mesh that gets parented to the player's hand. " +
             "Create a child GameObject on this pickup with the rifle model.")]
    [SerializeField] private GameObject equippedWeaponPrefab;

    [Tooltip("Empty Transform at the tip of the barrel — becomes PlayerCombat.muzzlePoint.")]
    [SerializeField] private Transform barrelTip;

    [Tooltip("Fine-tune position of the rifle in the player's hand.")]
    [SerializeField] private Vector3 weaponLocalPosition = new Vector3(0.05f, 0.02f, 0.12f);

    [Tooltip("Fine-tune rotation of the rifle in the player's hand (euler degrees).")]
    [SerializeField] private Vector3 weaponLocalRotation = new Vector3(0f, 90f, 0f);

    // Runtime reference to the equipped instance (kept for unequip/respawn)
    private static GameObject _equippedInstance;

    protected override void OnPickedUp(GameObject player)
    {
        var inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        bool firstPickup = !inv.HasRifle;

        int ammo = firstPickup ? initialAmmo : bonusAmmo;
        inv.CollectWeaponOrAmmo(ammo);

        PlayerCombat combat = player.GetComponentInChildren<PlayerCombat>();
        if (combat != null)
        {
            if (firstPickup)
            {
                combat.EquipWeapon();
                AttachWeaponToHand(player, combat);
            }
            combat.AddAmmo(ammo);
        }

        DestroyPickup();
    }

    // ─────────────────────────────────────────────────────────
    void AttachWeaponToHand(GameObject player, PlayerCombat combat)
    {
        // ── Find the right hand bone ─────────────────────────
        Transform hand = FindBoneRecursive(player.transform, handBoneName);
        if (hand == null)
        {
            Debug.LogWarning($"[WeaponPickup] Hand bone '{handBoneName}' not found. " +
                             "Check the exact bone name in your rig's hierarchy.");
            // Fallback: attach to player root so at least it's not floating
            hand = player.transform;
        }

        // ── Destroy previous instance if re-equipping ────────
        if (_equippedInstance != null)
            Destroy(_equippedInstance);

        // ── Instantiate the equipped weapon model ────────────
        GameObject source = equippedWeaponPrefab != null
            ? equippedWeaponPrefab
            : gameObject;   // fallback: use this pickup's own GO visuals

        // Parent to PLAYER ROOT (not hand bone) so WeaponAimer can use
        // clean world-space positioning without hand bone axis confusion.
        _equippedInstance = Instantiate(source, player.transform);
        _equippedInstance.name = "EquippedWeapon";

        // Remove FloatingPickup / WeaponPickup / Collider from the equipped copy
        foreach (var col in _equippedInstance.GetComponentsInChildren<Collider>())
            col.enabled = false;
        var fp = _equippedInstance.GetComponent<FloatingPickup>();
        if (fp != null) fp.enabled = false;
        var wp = _equippedInstance.GetComponent<WeaponPickup>();
        if (wp != null) wp.enabled = false;

        // Reset local transform — WeaponAimer owns position/rotation in LateUpdate
        _equippedInstance.transform.localPosition = Vector3.zero;
        _equippedInstance.transform.localRotation = Quaternion.identity;

        // ── Assign muzzle point so bullets come from barrel tip ──
        Transform muzzle = FindBoneRecursive(_equippedInstance.transform, "MuzzleTip");
        if (muzzle == null)
        {
            // Auto-create muzzle at front of weapon
            GameObject autoMuzzle = new GameObject("MuzzleTip");
            autoMuzzle.transform.SetParent(_equippedInstance.transform);
            autoMuzzle.transform.localPosition = new Vector3(0f, 0f, 0.5f); // forward
            muzzle = autoMuzzle.transform;
            Debug.Log("[WeaponPickup] MuzzleTip not found — auto-created 0.5 units forward of weapon.");
        }

        combat.muzzlePoint = muzzle;

        // ── Add WeaponAimer — drives position + rotation in LateUpdate ──
        WeaponAimer aimer    = _equippedInstance.AddComponent<WeaponAimer>();
        aimer.handBone       = hand;
        aimer.playerRoot     = player.transform;  // for player-local holdOffset
        aimer.aimCamera      = Camera.main;
        aimer.aimDistance    = 200f;
        aimer.rotationSpeed  = 0f;

        PlayerCombat pc = player.GetComponentInChildren<PlayerCombat>();
        if (pc != null) aimer.aimMask = pc.aimLayer;

        Debug.Log($"[WeaponPickup] Weapon attached to '{hand.name}'. MuzzlePoint → {muzzle.name}. WeaponAimer active.");
    }

    // ─────────────────────────────────────────────────────────
    static Transform FindBoneRecursive(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindBoneRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ── Called by PlayerHealth on respawn if keepWeaponOnRespawn=false ──
    public static void DestroyEquippedInstance()
    {
        if (_equippedInstance != null)
        {
            Destroy(_equippedInstance);
            _equippedInstance = null;
        }
    }
}