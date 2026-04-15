using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────
    public static CheckpointManager Instance { get; private set; }

    // ══════════════════════════════════════════════════════════
    // SAVED STATE — populated by SaveCheckpoint()
    // ══════════════════════════════════════════════════════════

    [System.Serializable]
    public class SavedState
    {
        // Player
        public float     playerHealth;
        public Vector3   respawnPosition;
        public Quaternion respawnRotation;

        // Inventory / Ammo (expand as needed)
        public int primaryAmmo;
        public int reserveAmmo;

        // Collectables: store IDs of collected items
        public List<string> collectedIDs = new List<string>();

        // Persistent destructions: IDs of objects destroyed BEFORE this checkpoint
        // (enemies killed, destructibles broken — should stay dead after reset)
        public HashSet<string> persistentIDs = new HashSet<string>();

        public bool HasData => respawnPosition != Vector3.zero || playerHealth > 0f;
    }

    private SavedState _saved = new SavedState();

    // Has the player ever touched a checkpoint?
    public bool HasCheckpoint => _saved.HasData;

    // ══════════════════════════════════════════════════════════
    // UNITY MESSAGES
    // ══════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Uncomment for multi-scene:
        // DontDestroyOnLoad(gameObject);
    }

    // ══════════════════════════════════════════════════════════
    // SAVE — called by Checkpoint.cs
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Called by a Checkpoint when the player enters its trigger.
    /// Snapshots current game state and stores the respawn position.
    /// </summary>
    public void SaveCheckpoint(Vector3 position, Quaternion rotation)
    {
        // ── Respawn position ──────────────────────────────────
        _saved.respawnPosition = position;
        _saved.respawnRotation = rotation;

        // ── Player health ─────────────────────────────────────
        PlayerHealth ph = FindPlayerHealth();
        if (ph != null)
            _saved.playerHealth = ph.CurrentHealth;

        // ── Ammo / Inventory ──────────────────────────────────
        // Uncomment and adapt to your inventory system:
        // var inv = FindObjectOfType<InventoryManager>();
        // if (inv != null) { _saved.primaryAmmo = inv.PrimaryAmmo; _saved.reserveAmmo = inv.ReserveAmmo; }

        // ── Collectables ──────────────────────────────────────
        // Copy current collected IDs from SceneStateTracker if present:
        SceneStateTracker sst = SceneStateTracker.Instance;
        if (sst != null)
        {
            // _saved.collectedIDs  = new List<string>(sst.CollectedIDs);  // requires SceneStateTracker patch
            _saved.persistentIDs = new HashSet<string>(sst.DestroyedIDs);
        }

        Debug.Log($"[CheckpointManager] Saved at {position} | HP={_saved.playerHealth:F0}");
    }

    // ══════════════════════════════════════════════════════════
    // LOAD — called by PlayerHealth.DoRespawn()
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Resets the scene to the last saved checkpoint state,
    /// then teleports the player to the saved respawn position.
    /// </summary>
    public void LoadCheckpoint()
    {
        if (!HasCheckpoint)
        {
            Debug.LogWarning("[CheckpointManager] LoadCheckpoint called but no checkpoint was saved.");
            return;
        }

        StartCoroutine(LoadRoutine());
    }

    IEnumerator LoadRoutine()
    {
        // ── Phase 1: Reset all IResettable objects in the scene ──
        // (enemies return to patrol, destructibles rebuild, pickups reappear)
        var resettables = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var mb in resettables)
        {
            if (mb is IResettable r)
                r.ResetState();
        }

        // Allow one frame for resets to propagate (NavMesh agents re-enable, etc.)
        yield return new WaitForEndOfFrame();

        // ── Phase 2: Re-apply persistent destructions ────────────
        // Objects killed/broken BEFORE this checkpoint should stay dead.
        if (_saved.persistentIDs != null && _saved.persistentIDs.Count > 0)
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is IResettable r && _saved.persistentIDs.Contains(r.ResettableID))
                {
                    if      (mb is DestructibleSurface ds) ds.ForceDestroy();
                    else if (mb is EnemyAI             ec) ec.ForceKill();
                    else    mb.gameObject.SetActive(false);
                }
            }
        }

        // ── Phase 3: Restore player ───────────────────────────────
        yield return new WaitForEndOfFrame();

        RestorePlayer();
    }

    void RestorePlayer()
    {
        GameObject playerGO = GameObject.FindWithTag("Player");
        if (playerGO == null)
        {
            Debug.LogError("[CheckpointManager] No GameObject tagged 'Player' found.");
            return;
        }

        // ── Teleport to last checkpoint position ──────────────
        CharacterController cc = playerGO.GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;

        playerGO.transform.SetPositionAndRotation(
            _saved.respawnPosition,
            _saved.respawnRotation);

        if (cc != null) cc.enabled = true;

        // ── Restore HP ────────────────────────────────────────
        PlayerHealth ph = playerGO.GetComponentInChildren<PlayerHealth>();
        if (ph != null)
        {
            ph.SetHealth(_saved.playerHealth > 0f ? _saved.playerHealth : ph.MaxHealth);

            // Re-enable all player components disabled on death
            var movement = playerGO.GetComponentInChildren<StarterAssets.ThirdPersonController>();
            var combat   = playerGO.GetComponentInChildren<PlayerCombat>();
            if (movement != null) movement.enabled = true;
            if (combat   != null) combat.enabled   = true;
        }

        // ── Restore ammo / inventory ──────────────────────────
        // Uncomment and adapt:
        // var inv = playerGO.GetComponentInChildren<InventoryManager>();
        // if (inv != null) { inv.PrimaryAmmo = _saved.primaryAmmo; inv.ReserveAmmo = _saved.reserveAmmo; }

        // ── Restore collectables state ────────────────────────
        SceneStateTracker sst = SceneStateTracker.Instance;
        if (sst != null && _saved.collectedIDs != null)
            // sst.RestoreCollected(_saved.collectedIDs); // requires SceneStateTracker patch

        // ── Notify PlayerHealth respawn is complete ───────────
        ph?.OnRespawned?.Invoke();

        Debug.Log($"[CheckpointManager] Loaded. Player → {_saved.respawnPosition} | HP={_saved.playerHealth:F0}");
    }

    // ══════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════

    PlayerHealth FindPlayerHealth()
    {
        GameObject p = GameObject.FindWithTag("Player");
        return p != null ? p.GetComponentInChildren<PlayerHealth>() : null;
    }

    /// <summary>
    /// Called by SceneStateTracker when an object is permanently destroyed
    /// (enemy killed, surface broken) — tracked between checkpoints.
    /// </summary>
    public void RegisterPersistentDestruction(string id)
    {
        if (!string.IsNullOrEmpty(id))
            _saved.persistentIDs.Add(id);
    }

    /// <summary>Returns the current saved respawn position (for debug/UI).</summary>
    public Vector3 RespawnPosition => _saved.respawnPosition;
}