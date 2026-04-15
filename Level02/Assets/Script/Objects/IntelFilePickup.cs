using UnityEngine;

public class IntelFilePickup : FloatingPickup
{
    [Header("Intel Content")]
    [SerializeField] private IntelData intelData;

    protected override void OnPickedUp(GameObject player)
    {
        var inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        inv.CollectIntel(intelData);
        DestroyPickup();

        GetComponent<PersistentPickup>()?.Collect();
        gameObject.SetActive(false);
    }
}