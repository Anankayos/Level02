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

        // PlayerInventory decides: equip rifle OR add ammo
        int ammo = inv.HasRifle ? bonusAmmo : initialAmmo;
        inv.CollectWeaponOrAmmo(ammo);
        DestroyPickup();
    }
}