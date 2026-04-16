using UnityEngine;

public class TutorialTrigger : MonoBehaviour
{
    [Header("Message")]
    [TextArea(2, 4)]
    [SerializeField] private string message    = "Use WASD to move.";
    [SerializeField] private string inputHint  = "";    // e.g. "Press E to interact"
    [SerializeField] private float  duration   = 5f;   // -1 = manual dismiss only

    [Header("Behaviour")]
    [SerializeField] private bool triggerOnce  = true;  // never shows again after first time
    [SerializeField] private bool disableAfter = true;  // disable collider after trigger

    private bool _triggered;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (_triggered && triggerOnce)   return;

        _triggered = true;
        GameEvents.FireShowTutorial(message, inputHint, duration);

        if (disableAfter)
            GetComponent<Collider>().enabled = false;
    }

    // Optional: visualise the trigger zone in the Editor
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.15f);
        Gizmos.matrix = transform.localToWorldMatrix;

        var col = GetComponent<BoxCollider>();
        if (col != null) Gizmos.DrawCube(col.center, col.size);

        var sph = GetComponent<SphereCollider>();
        if (sph != null) Gizmos.DrawSphere(sph.center, sph.radius);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }
}