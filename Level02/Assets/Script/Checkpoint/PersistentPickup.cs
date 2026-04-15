using System;
using UnityEngine;

public class PersistentPickup : MonoBehaviour, IResettable
{
    // ── IResettable ───────────────────────────────────────────
    private string _id;
    public string ResettableID =>
        string.IsNullOrEmpty(_id) ? (_id = Guid.NewGuid().ToString()) : _id;

    // ── State ─────────────────────────────────────────────────
    private bool _collected;

    // Optional: called after collection (wire in Inspector or subscribe in code)
    public event Action OnPickedUp;

    // ─────────────────────────────────────────────────────────
    // PUBLIC API — call this from your pickup / interaction code
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Mark this item as permanently collected.
    /// Call this wherever you currently disable the object or add it to inventory.
    ///
    /// Example — in your existing KeyPickup.cs OnTriggerEnter:
    ///   GetComponent&lt;PersistentPickup&gt;()?.Collect();
    ///   gameObject.SetActive(false);
    /// </summary>
    public void Collect()
    {
        if (_collected) return;
        _collected = true;

        // Register as permanently gone — survives checkpoint resets
        SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);

        OnPickedUp?.Invoke();

        Debug.Log($"[PersistentPickup] '{name}' collected and registered as permanent.");
    }

    // ─────────────────────────────────────────────────────────
    // IResettable
    // ─────────────────────────────────────────────────────────

    public void SaveInitialState() { }

    public void ResetState()
    {
        // CheckpointManager's "re-apply persistent" phase calls SetActive(false)
        // on collected items — so ResetState just means "come back if not collected"
        if (!_collected)
        {
            gameObject.SetActive(true);
            Debug.Log($"[PersistentPickup] '{name}' reset — available again.");
        }
        // If _collected == true: CheckpointManager will immediately call
        // SetActive(false) on the next frame via the persistent IDs pass
    }

    public void ForceCollect()
    {
        _collected = true;
        gameObject.SetActive(false);
    }
}