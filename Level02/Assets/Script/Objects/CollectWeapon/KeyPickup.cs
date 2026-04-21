using UnityEngine;

public class KeyPickup : FloatingPickup
{
    [Header("Key Type")]
    [SerializeField] private KeyType keyType = KeyType.KeyA;

    // Track collection locally so ResetState can block re-enable
    private bool _collected;

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

        _collected = true;

        // Register BOTH the FloatingPickup ID (FP:) AND the PersistentPickup ID (PP:)
        // so Phase 2 of CheckpointManager can match and suppress this object
        // regardless of which IResettable component it finds first.
        DestroyPickup(); // registers FP: path + calls SetActive(false)
        GetComponent<PersistentPickup>()?.Collect(); // registers PP: path
    }

    // Override so a collected key is never re-enabled by Phase 1,
    // even if its ID somehow slips through Phase 2 matching.
    public override void ResetState()
    {
        if (_collected)
            gameObject.SetActive(false);
        else
            base.ResetState();
    }
}
