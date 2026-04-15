using UnityEngine;

public class KeyPickup : FloatingPickup
{
    [Header("Key Type")]
    [SerializeField] private KeyType keyType = KeyType.KeyA;

    protected override void OnPickedUp(GameObject player)
    {
        var inv = player.GetComponent<PlayerInventory>();
        if (inv == null) return;

        inv.CollectKey(keyType);
        DestroyPickup();

        GetComponent<PersistentPickup>()?.Collect();
        gameObject.SetActive(false);
    }
}