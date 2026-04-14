using UnityEngine;

public class Checkpoint : MonoBehaviour, IResettable
{
    [Header("Visual")]
    [SerializeField] private GameObject activeVFX;    // glowing beam/halo when saved
    [SerializeField] private GameObject inactiveVFX;  // idle state indicator

    private bool   _isActive;
    private string _id;
    public  string ResettableID =>
        string.IsNullOrEmpty(_id) ? (_id = System.Guid.NewGuid().ToString()) : _id;

    private void Start() => SetVisual(false);

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var health = other.GetComponent<PlayerHealth>();
        var inv    = other.GetComponent<PlayerInventory>();

        CheckpointManager.Instance?.SaveCheckpoint(
            other.transform.position,
            other.transform.rotation,
            health, inv,
            SceneStateTracker.Instance?.GetDestroyedSnapshot()
                ?? new System.Collections.Generic.HashSet<string>()
        );
        SetVisual(true);
    }

    private void SetVisual(bool active)
    {
        _isActive = active;
        if (activeVFX)   activeVFX.SetActive(active);
        if (inactiveVFX) inactiveVFX.SetActive(!active);
    }

    public void SaveInitialState() { }
    public void ResetState()       => SetVisual(false);
}