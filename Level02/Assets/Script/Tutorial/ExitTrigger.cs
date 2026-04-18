using UnityEngine;

public class ExitTrigger : MonoBehaviour {
    private void OnTriggerEnter(Collider other) {
        Debug.Log($"Trigger hit by: {other.name} (Tag: '{other.tag}')", other.gameObject);
        
        if (other.CompareTag("Player")) {
            Debug.Log("Victory! Quitting game.", this);
            Application.Quit();
        } else {
            Debug.Log($"Not Player—ignored. Expected 'Player', got '{other.tag}'");
        }
    }
}