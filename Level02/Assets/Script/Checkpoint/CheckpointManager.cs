using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CheckpointManager : MonoBehaviour
{
    public static CheckpointManager Instance { get; private set; }

    [System.Serializable]
    public class SavedState
    {
        public float      playerHealth;
        public Vector3    respawnPosition;
        public Quaternion respawnRotation;
        public int        primaryAmmo;
        public int        reserveAmmo;
        public bool       hadWeapon;
        public List<string>    collectedIDs  = new List<string>();
        public HashSet<string> persistentIDs = new HashSet<string>();
        public bool HasData => respawnPosition != Vector3.zero || playerHealth > 0f;
    }

    private SavedState _saved = new SavedState();
    public bool HasCheckpoint => _saved.HasData;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SaveCheckpoint(Vector3 position, Quaternion rotation)
    {
        _saved.respawnPosition = position;
        _saved.respawnRotation = rotation;

        PlayerHealth ph = FindPlayerHealth();
        if (ph != null) _saved.playerHealth = ph.CurrentHealth;

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

        // DEBUG: print every ID saved into persistentIDs
        Debug.Log($"[CheckpointManager] SAVE — {_saved.persistentIDs.Count} persistent IDs:");
        foreach (var id in _saved.persistentIDs)
            Debug.Log($"  SAVED ID: {id}");

        GameEvents.FireCheckpointReached("Checkpoint Saved");
        Debug.Log($"[CheckpointManager] Saved at {position} | HP={_saved.playerHealth:F0}");
    }

    public void LoadCheckpoint()
    {
        if (!HasCheckpoint) { Debug.LogWarning("[CheckpointManager] No checkpoint saved."); return; }
        StartCoroutine(LoadRoutine());
    }

    IEnumerator LoadRoutine()
    {
        SceneStateTracker.Instance?.RestoreSnapshot(
            new HashSet<string>(_saved.persistentIDs));

        // DEBUG: confirm what IDs we are trying to suppress
        Debug.Log($"[CheckpointManager] LOAD — will suppress {_saved.persistentIDs.Count} IDs:");
        foreach (var id in _saved.persistentIDs)
            Debug.Log($"  SUPPRESS ID: {id}");

        // Phase 1: Reset ALL IResettable objects
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (mb is IResettable r) r.ResetState();

        yield return new WaitForEndOfFrame();

        // Phase 2: Re-suppress objects that were already gone AT save time
        if (_saved.persistentIDs != null && _saved.persistentIDs.Count > 0)
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!(mb is IResettable r)) continue;

                string foundID = r.ResettableID;
                bool matched = _saved.persistentIDs.Contains(foundID);

                // DEBUG: log every IResettable found and whether it matched
                if (mb is DestructibleSurface || mb is PersistentPickup || mb is KeyPickup)
                    Debug.Log($"  FOUND [{mb.GetType().Name}] '{mb.name}' ID='{foundID}' matched={matched}");

                if (!matched) continue;

                if      (mb is DestructibleSurface ds) ds.Suppress();
                else if (mb is EnemyAI             ec) ec.ForceKill();
                else if (mb is PersistentPickup    pp) pp.Suppress();
                else    mb.gameObject.SetActive(false);
            }
        }

        yield return new WaitForEndOfFrame();
        RestorePlayer();
    }

    void RestorePlayer()
    {
        GameObject playerGO = GameObject.FindWithTag("Player");
        if (playerGO == null) { Debug.LogError("[CheckpointManager] No GameObject tagged 'Player' found."); return; }

        CharacterController cc = playerGO.GetComponentInChildren<CharacterController>();
        if (cc != null) cc.enabled = false;
        playerGO.transform.SetPositionAndRotation(_saved.respawnPosition, _saved.respawnRotation);
        if (cc != null) cc.enabled = true;

        PlayerHealth ph = playerGO.GetComponentInChildren<PlayerHealth>();
        if (ph != null) ph.SetHealth(_saved.playerHealth > 0f ? _saved.playerHealth : ph.MaxHealth);

        var movement = playerGO.GetComponentInChildren<StarterAssets.ThirdPersonController>();
        var combat   = playerGO.GetComponentInChildren<PlayerCombat>();

        if (movement != null) movement.enabled = true;
        if (combat   != null) combat.enabled   = true;
        if (combat   != null) combat.ForceResetAim();

        if (combat != null && _saved.hadWeapon)
        {
            combat.EquipWeapon();
            combat.RestoreAmmo(_saved.primaryAmmo);
        }

        ph?.OnRespawned?.Invoke();
        Debug.Log($"[CheckpointManager] Loaded. Player → {_saved.respawnPosition} | HP={_saved.playerHealth:F0}");
    }

    PlayerHealth FindPlayerHealth()
    {
        GameObject p = GameObject.FindWithTag("Player");
        return p != null ? p.GetComponentInChildren<PlayerHealth>() : null;
    }

    public void RegisterPersistentDestruction(string id)
    {
        if (!string.IsNullOrEmpty(id)) _saved.persistentIDs.Add(id);
    }

    public Vector3 RespawnPosition => _saved.respawnPosition;
}
