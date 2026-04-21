using System;
using UnityEngine;

public class PersistentPickup : MonoBehaviour, IResettable
{
    // ── IResettable ───────────────────────────────────────────
    // IMPORTANT: ID must be stable across play sessions so that
    // the ID saved in persistentIDs at checkpoint-save time still
    // matches the ID present at checkpoint-load time.
    // We use the full scene hierarchy path which is unique per
    // scene object and never changes at runtime.
    public string ResettableID => GetScenePath();

    private string GetScenePath()
    {
        var t = transform;
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return "PP:" + path;
    }

    // ── State ─────────────────────────────────────────────────
    private bool _collected;

    public event Action OnPickedUp;

    // ─────────────────────────────────────────────────────────
    // PUBLIC API
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// Mark this item as permanently collected.
    /// Call from your pickup / interaction code.
    /// Example (KeyPickup.cs):
    ///   GetComponent<PersistentPickup>()?.Collect();
    ///   gameObject.SetActive(false);
    /// </summary>
    public void Collect()
    {
        if (_collected) return;
        _collected = true;

        SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);
        OnPickedUp?.Invoke();

        Debug.Log($"[PersistentPickup] '{name}' collected — ID: {ResettableID}");
    }

    // ─────────────────────────────────────────────────────────
    // IResettable
    // ─────────────────────────────────────────────────────────

    public void SaveInitialState() { }

    public void ResetState()
    {
        // Phase 2 of LoadRoutine will call SetActive(false) for any object
        // whose ResettableID is in persistentIDs. For objects NOT in that
        // set (i.e. collected after the checkpoint save), we re-enable them.
        _collected = false;
        gameObject.SetActive(true);
        Debug.Log($"[PersistentPickup] '{name}' reset — available again.");
    }

    public void ForceCollect()
    {
        _collected = true;
        gameObject.SetActive(false);
    }
}
