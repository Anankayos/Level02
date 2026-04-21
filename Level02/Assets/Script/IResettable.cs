/// <summary>
/// Implement on any MonoBehaviour that needs to be reset when the player
/// loads a checkpoint. CheckpointManager.LoadRoutine() calls ResetState()
/// on every IResettable found in the scene, then re-suppresses any object
/// whose ResettableID is in the persistent-destroyed set.
/// </summary>
public interface IResettable
{
    /// <summary>Stable, scene-unique ID used to track permanent destructions.</summary>
    string ResettableID { get; }

    /// <summary>
    /// Called once when the checkpoint is first saved so the object can
    /// snapshot any extra state it needs. May be left empty.
    /// </summary>
    void SaveInitialState();

    /// <summary>
    /// Restore the object to the state it had when the checkpoint was saved.
    /// For pickups this means SetActive(true); for enemies this means reviving.
    /// CheckpointManager will immediately call SetActive(false) on the next
    /// frame for objects that appear in the persistent-destroyed list.
    /// </summary>
    void ResetState();
}
