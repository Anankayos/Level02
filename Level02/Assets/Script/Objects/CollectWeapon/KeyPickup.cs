using UnityEngine;

public class KeyPickup : FloatingPickup
{
    [Header("Key Type")]
    [SerializeField] private KeyType keyType = KeyType.KeyA;

    protected override void OnPickedUp(GameObject player)
    {
        var inv = player.GetComponentInParent<PlayerInventory>();
        if (inv == null)
            inv = player.GetComponent<PlayerInventory>();

        if (inv == null)
        {
            Debug.LogWarning("[KeyPickup] PlayerInventory not found on player.");
            return;
        }

        inv.CollectKey(keyType);

        // Mark as permanently collected
        GetComponent<PersistentPickup>()?.Collect();

        // Do NOT respawn
        gameObject.SetActive(false);
    }
}