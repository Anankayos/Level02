using System;
using UnityEngine;

public class PersistentPickup : MonoBehaviour, IResettable
{
    // ── IResettable ───────────────────────────────────────────
    public string ResettableID => GetScenePath();

    private string GetScenePath()
    {
        var t = transform;
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return "PP:" + path;
    }

    // ── State ─────────────────────────────────────────────────
    private bool _collected;

    public event Action OnPickedUp;

    // ── Public API ───────────────────────────────────────────

    /// <summary>Mark as permanently collected (player just picked it up).</summary>
    public void Collect()
    {
        if (_collected) return;
        _collected = true;
        SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);
        OnPickedUp?.Invoke();
        Debug.Log($"[PersistentPickup] '{name}' collected — ID: {ResettableID}");
    }

    /// <summary>
    /// Silent re-suppress called by CheckpointManager Phase 2.
    /// Does NOT touch SceneStateTracker — the tracker was already
    /// restored to the checkpoint snapshot before Phase 1 ran.
    /// </summary>
    public void Suppress()
    {
        _collected = true;
        gameObject.SetActive(false);
    }

    // ── IResettable ───────────────────────────────────────────

    public void SaveInitialState() { }

    public void ResetState()
    {
        _collected = false;
        gameObject.SetActive(true);
    }

    public void ForceCollect()
    {
        _collected = true;
        gameObject.SetActive(false);
    }
}
