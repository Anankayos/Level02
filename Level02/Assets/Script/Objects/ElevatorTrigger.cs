using UnityEngine;

public class ElevatorTrigger : MonoBehaviour {
    private PlatformMoving platform;

    void Start() {
        platform = GetComponentInParent<PlatformMoving>();
    }

    void OnTriggerEnter(Collider other) {
        if (other.CompareTag("Player")) {
            other.transform.SetParent(transform.parent);  // Stick player to elevator
            Debug.Log("Player on elevator");
        }
    }

    void OnTriggerExit(Collider other) {
        if (other.CompareTag("Player")) {
            other.transform.SetParent(null);  // Release player
            Debug.Log("Player left elevator");
        }
    }
}