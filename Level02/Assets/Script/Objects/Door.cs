using UnityEngine;

public class Door : MonoBehaviour
{
    [Header("Lock")]
    [SerializeField] private KeyType requiredKey;

    [Header("Animation")]
    [SerializeField] private float openAngle = 90f;     // degrees to rotate on Y
    [SerializeField] private float openSpeed = 3f;

    [Header("Feedback")]
    [SerializeField] private GameObject lockedIndicator; // a UI world-space icon above door

    private bool       _unlocked;
    private bool       _fullyOpen;
    private Quaternion _closedRot;
    private Quaternion _openRot;
    private Collider   _doorCollider;

    private void Start()
    {
        _closedRot = transform.rotation;
        _openRot = Quaternion.Euler(
            transform.eulerAngles.x,
            transform.eulerAngles.y + openAngle,
            transform.eulerAngles.z
        );
        _doorCollider = GetComponent<Collider>();

        if (lockedIndicator) lockedIndicator.SetActive(true);
    }

    private void Update()
    {
        if (!_unlocked || _fullyOpen) return;

        // Smoothly swing the door open
        transform.rotation = Quaternion.Lerp(
            transform.rotation, _openRot, Time.deltaTime * openSpeed
        );

        if (Quaternion.Angle(transform.rotation, _openRot) < 0.5f)
        {
            transform.rotation = _openRot;
            _fullyOpen         = true;

            // Disable the door's physical collider so the player can walk through
            if (_doorCollider) _doorCollider.enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_unlocked || !other.CompareTag("Player")) return;

        var inv = other.GetComponent<PlayerInventory>();
        if (inv != null && inv.HasKey(requiredKey))
        {
            Unlock();
        }
        else
        {
            Debug.Log($"[Door] Locked — requires {requiredKey}.");
            // TODO: Play a "locked" sound or shake animation here
        }
    }

    private void Unlock()
    {
        _unlocked = true;
        if (lockedIndicator) lockedIndicator.SetActive(false);
        Debug.Log($"[Door] Unlocked with {requiredKey}!");
    }
}