using System.Collections.Generic;
using UnityEngine;

// Singleton — tracks every destroyed/collected ID at runtime
public class SceneStateTracker : MonoBehaviour
{
    public static SceneStateTracker Instance { get; private set; }

    private readonly HashSet<string> _destroyedIDs = new();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    public void RegisterDestroyed(string id)         => _destroyedIDs.Add(id);
    public HashSet<string> GetDestroyedSnapshot()    => new HashSet<string>(_destroyedIDs);
    public void RestoreSnapshot(HashSet<string> snap)
    {
        _destroyedIDs.Clear();
        foreach (var id in snap) _destroyedIDs.Add(id);
    }
}