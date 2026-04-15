using UnityEngine;

public class TrainRoofTrigger : MonoBehaviour
{
    private TrainMover _mover;

    void Awake()
    {
        _mover = GetComponentInParent<TrainMover>();
        if (_mover == null)
            Debug.LogError("[TrainRoofTrigger] No TrainMover found in parent hierarchy.");
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        var cc = other.GetComponentInParent<CharacterController>();
        if (cc != null) _mover?.SetRider(cc);
        Debug.Log("[Train] Player on top — riding enabled.");
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        _mover?.SetRider(null);
        Debug.Log("[Train] Player left top.");
    }
}