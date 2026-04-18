using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────
    public static CheckpointManager Instance { get; private set; }

    // ══════════════════════════════════════════════════════════
    // SAVED STATE
    // ══════════════════════════════════════════════════════════

    [System.Serializable]
    public class SavedState
    {
        public float      playerHealth;
        public Vector3    respawnPosition;
        public Quaternion respawnRotation;

        public int  primaryAmmo;
        public int  reserveAmmo;
        public bool hadWeapon;

        public List<string>    collectedIDs  = new List<string>();
        public HashSet<string> persistentIDs = new HashSet<string>();

        public bool HasData => respawnPosition != Vector3.zero || playerHealth > 0f;
    }

    private SavedState _saved = new SavedState();

    public bool HasCheckpoint => _saved.HasData;

    // ══════════════════════════════════════════════════════════
    // UNITY MESSAGES
    // ══════════════════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ══════════════════════════════════════════════════════════
    // SAVE
    // ══════════════════════════════════════════════════════════

    public void SaveCheckpoint(Vector3 position, Quaternion rotation)
    {
        _saved.respawnPosition = position;
        _saved.respawnRotation = rotation;

        PlayerHealth ph = FindPlayerHealth();
        if (ph != null)
            _saved.playerHealth = ph.CurrentHealth;

        GameObject playerGO = GameObject.FindWithTag("Player");
        if (playerGO != null)
        {
            PlayerCombat combat = playerGO.GetComponentInChildren<PlayerCombat>();
            if (combat != null)
            {
                _saved.primaryAmmo = combat.CurrentAmmo;
                _saved.reserveAmmo = combat.ReserveAmmo;
                _saved.hadWeapon   = combat.HasWeapon;
            }
        }

        SceneStateTracker sst = SceneStateTracker.Instance;
        if (sst != null)
            _saved.persistentIDs = new HashSet<string>(sst.DestroyedIDs);

        GameEvents.FireCheckpointReached("Checkpoint Saved");

        Debug.Log($"[CheckpointManager] Saved at {position} | HP={_saved.playerHealth:F0} | Ammo={_saved.primaryAmmo}");
    }

    // ══════════════════════════════════════════════════════════
    // LOAD
    // ══════════════════════════════════════════════════════════

    public void LoadCheckpoint()
    {
        if (!HasCheckpoint)
        {
            Debug.LogWarning("[CheckpointManager] No checkpoint saved.");
            return;
        }
        StartCoroutine(LoadRoutine());
    }

    IEnumerator LoadRoutine()
    {
        // Phase 1: Reset ALL IResettable objects including boss
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (mb is IResettable r) r.ResetState();

        yield return new WaitForEndOfFrame();

        // Phase 2: Re-apply persistent destructions
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

        yield return new WaitForEndOfFrame();

        // Phase 3: Restore player
        RestorePlayer();
    }

    // ══════════════════════════════════════════════════════════
    // RESTORE PLAYER
    // ══════════════════════════════════════════════════════════

    void RestorePlayer()
    {
        GameObject playerGO = GameObject.FindWithTag("Player");
        if (playerGO == null)
        {
            Debug.LogError("[CheckpointManager] No GameObject tagged 'Player' found.");
            return;
        }

        // Teleport
        CharacterController cc = playerGO.GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;

        playerGO.transform.SetPositionAndRotation(
            _saved.respawnPosition,
            _saved.respawnRotation);

        if (cc != null) cc.enabled = true;

        // Step 1: restore health first so IsDead = false
        PlayerHealth ph = playerGO.GetComponentInChildren<PlayerHealth>();
        if (ph != null)
            ph.SetHealth(_saved.playerHealth > 0f ? _saved.playerHealth : ph.MaxHealth);

        // Step 2: re-enable movement and combat
        var movement = playerGO.GetComponentInChildren<StarterAssets.ThirdPersonController>();
        var combat   = playerGO.GetComponentInChildren<PlayerCombat>();

        if (movement != null) movement.enabled = true;
        if (combat   != null) combat.enabled   = true;

        // Step 3: force aim reset AFTER component is enabled
        if (combat != null) combat.ForceResetAim();

        // Step 4: restore ammo
        if (combat != null && _saved.hadWeapon)
        {
            combat.EquipWeapon();
            combat.RestoreAmmo(_saved.primaryAmmo);
        }

        // Step 5: notify respawn complete
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

    public void RegisterPersistentDestruction(string id)
    {
        if (!string.IsNullOrEmpty(id))
            _saved.persistentIDs.Add(id);
    }

    public Vector3 RespawnPosition => _saved.respawnPosition;
}