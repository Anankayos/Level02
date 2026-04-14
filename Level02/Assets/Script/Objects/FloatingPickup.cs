using UnityEngine;

public abstract class FloatingPickup : MonoBehaviour
{
    [Header("Float Animation")]
    [SerializeField] private float floatAmplitude = 0.25f;   // vertical travel range
    [SerializeField] private float floatFrequency = 1.5f;    // oscillations per second
    [SerializeField] private float rotationSpeed  = 60f;     // degrees/sec on Y axis
    [SerializeField] private float phaseOffset    = 0f;      // stagger multiple pickups

    [Header("Detection")]
    [SerializeField] private string playerTag = "Player";

    private Vector3 _originPosition;

    protected virtual void Start()
    {
        _originPosition = transform.position;
        // Random phase so items in a group don't bob in sync
        phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        // Bob up and down
        float newY = _originPosition.y
                   + Mathf.Sin((Time.time * floatFrequency) + phaseOffset) * floatAmplitude;
        transform.position = new Vector3(
            transform.position.x,
            newY,
            transform.position.z
        );

        // Spin
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }
    // Called by Unity's physics system when player walks over the trigger collider
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        OnPickedUp(other.gameObject);
    }
    private string _pickupID;
public string ResettableID =>
    string.IsNullOrEmpty(_pickupID)
        ? (_pickupID = System.Guid.NewGuid().ToString())
        : _pickupID;

public virtual void SaveInitialState() { }
public virtual void ResetState() => gameObject.SetActive(true);

    // Each subclass defines what happens on pickup
    protected abstract void OnPickedUp(GameObject player);

    // Call this at the end of OnPickedUp to remove the item from the world
    protected void DestroyPickup()
{
    SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);
    // TODO: Instantiate particle VFX here
    gameObject.SetActive(false); // SetActive keeps IResettable alive for resets
}
}