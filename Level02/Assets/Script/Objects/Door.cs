using UnityEngine;

public class Door : MonoBehaviour
{
    [Header("Lock")]
    [SerializeField] private KeyType requiredKey;

    [Header("Door Target")]
    [Tooltip("If null, this GameObject is disabled. Useful if this script is on a trigger child.")]
    [SerializeField] private GameObject doorToHide;

    [Header("Feedback")]
    [SerializeField] private GameObject lockedIndicator;

    private bool _unlocked;

    private void Start()
    {
        if (doorToHide == null)
            doorToHide = gameObject;

        if (lockedIndicator) lockedIndicator.SetActive(true);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_unlocked || !other.CompareTag("Player")) return;

        var inv = other.GetComponentInParent<PlayerInventory>();
        if (inv == null)
            inv = other.GetComponent<PlayerInventory>();

        if (inv != null && inv.HasKey(requiredKey))
        {
            Unlock();
        }
        else
        {
            Debug.Log($"[Door] Locked — requires {requiredKey}.");
        }
    }

    private void Unlock()
    {
        _unlocked = true;

        if (lockedIndicator) lockedIndicator.SetActive(false);

        Debug.Log($"[Door] Unlocked with {requiredKey}!");

        if (doorToHide != null)
            doorToHide.SetActive(false);
    }
}