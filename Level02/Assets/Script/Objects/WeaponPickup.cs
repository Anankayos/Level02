using UnityEngine;

public class WeaponPickup : FloatingPickup
{
    [Header("Ammo")]
    [SerializeField] private int initialAmmo = 30;  // given when rifle is first picked up
    [SerializeField] private int bonusAmmo   = 30;  // given on every subsequent pickup

    protected override void OnPickedUp(GameObject player)
    {
        var inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        bool firstPickup = !inv.HasRifle;

        // PlayerInventory decides: equip rifle OR add ammo
        int ammo = firstPickup ? initialAmmo : bonusAmmo;
        inv.CollectWeaponOrAmmo(ammo);

        // ── Sync ammo with PlayerCombat (fires OnAmmoChanged → HUD) ──
        PlayerCombat combat = player.GetComponentInChildren<PlayerCombat>();
        if (combat != null)
        {
            if (firstPickup) combat.EquipWeapon();
            combat.AddAmmo(ammo);  // works for both first pickup and ammo refill
        }

        // Respawns normally (FloatingPickup handles it) — no PersistentPickup needed
        DestroyPickup();
    }
}