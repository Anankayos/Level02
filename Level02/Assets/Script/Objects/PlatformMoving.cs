using UnityEngine;
using System.Collections;

public class PlatformMoving : MonoBehaviour {
    public Transform[] waypoints;  // Array of empty GameObjects for positions (up/down)
    public float speed = 2f;
    private int currentWaypoint = 0;
    private bool moving = false;

    public void StartMovement() {
        moving = true;
        StartCoroutine(MoveToWaypoint());
    }

    IEnumerator MoveToWaypoint() {
        while (moving) {
            transform.position = Vector3.MoveTowards(transform.position, waypoints[currentWaypoint].position, speed * Time.deltaTime);
            if (Vector3.Distance(transform.position, waypoints[currentWaypoint].position) < 0.1f) {
                currentWaypoint = (currentWaypoint + 1) % waypoints.Length;
            }
            yield return null;
        }
    }

    // Call this from BossAI to stop if needed (e.g., player exits)
    public void StopMovement() {
        moving = false;
    }
}