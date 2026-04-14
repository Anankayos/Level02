using System.Collections.Generic;
using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance { get; private set; }

    private CheckpointData _saved;
    public  bool           HasCheckpoint => _saved != null;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else Destroy(gameObject);
    }

    // ── Called when player steps on a Checkpoint trigger ────────
    public void SaveCheckpoint(
        Vector3 pos, Quaternion rot,
        PlayerHealth health, PlayerInventory inv,
        HashSet<string> destroyedBeforeThis)
    {
        _saved = new CheckpointData
        {
            playerPosition  = pos,
            playerRotation  = rot,
            playerHealth    = health.CurrentHealth,
            hasRifle        = inv.HasRifle,
            ammo            = inv.Ammo,
            atmCards        = inv.ATMCards,
            collectedIntel  = new List<IntelData>(inv.Intel),
            persistentIDs   = new HashSet<string>(destroyedBeforeThis)
        };
        foreach (KeyType kt in System.Enum.GetValues(typeof(KeyType)))
            if (inv.HasKey(kt)) _saved.collectedKeys.Add(kt);

        Debug.Log($"[CheckpointManager] Saved at {pos}");
    }

    // ── Called on player death ───────────────────────────────────
    public void LoadCheckpoint()
    {
        if (_saved == null) { Debug.LogWarning("[CheckpointManager] No save found."); return; }

        // Phase 1 — Reset everything to initial state
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb is IResettable r) r.ResetState();

        // Phase 2 — Re-apply persistent pre-checkpoint destructions
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (mb is not IResettable r) continue;
            if (!_saved.persistentIDs.Contains(r.ResettableID)) continue;

            if      (mb is DestructibleSurface ds) ds.ForceDestroy();
            else if (mb is EnemyAI ec) ec.ForceKill();
            else                                    mb.gameObject.SetActive(false);
        }

        // Phase 3 — Sync scene tracker
        SceneStateTracker.Instance?.RestoreSnapshot(_saved.persistentIDs);

        // Phase 4 — Restore player
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;

        player.transform.SetPositionAndRotation(_saved.playerPosition, _saved.playerRotation);
        player.GetComponent<PlayerHealth>()?.SetHealth(_saved.playerHealth);
        player.GetComponent<PlayerInventory>()?.RestoreFromCheckpoint(_saved);

        Debug.Log("[CheckpointManager] Checkpoint loaded.");
    }
}