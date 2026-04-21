using System;
using UnityEngine;

public class PersistentPickup : MonoBehaviour, IResettable
{
    public string ResettableID => GetScenePath();

    private string GetScenePath()
    {
        var t = transform;
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return "PP:" + path;
    }

    private bool _collected;
    public event Action OnPickedUp;

    /// <summary>Mark as permanently collected (player just picked it up).</summary>
    public void Collect()
    {
        if (_collected) return;
        _collected = true;
        SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);

        // KEY FIX: write directly into the saved checkpoint so this item
        // stays gone even if no new checkpoint is reached after collection.
        CheckpointManager.Instance?.RegisterPersistentDestruction(ResettableID);

        OnPickedUp?.Invoke();
        Debug.Log($"[PersistentPickup] '{name}' collected — ID: {ResettableID}");
    }

    /// <summary>Silent re-suppress by CheckpointManager Phase 2. No tracker writes.</summary>
    public void Suppress()
    {
        _collected = true;
        gameObject.SetActive(false);
    }

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
