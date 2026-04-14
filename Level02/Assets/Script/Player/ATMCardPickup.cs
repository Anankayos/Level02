using UnityEngine;

public class ATMCardPickup : FloatingPickup
{
    protected override void OnPickedUp(GameObject player)
    {
        var inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        inv.CollectATMCard();
        DestroyPickup();
    }
}