using UnityEngine;

public class TutorialZone : MonoBehaviour
{
    [Header("Tutorial")]
    [SerializeField] private TutorialData tutorialData;
    [SerializeField] private bool triggerOnce = true;

    [Header("Detection")]
    [SerializeField] private Transform player;
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float radius = 3f;
    [SerializeField] private bool useXZOnly = true;

    private bool _triggered;
    private bool _wasInside;

    private void Start()
    {
        FindPlayer();
    }

    private void Update()
    {
        if (triggerOnce && _triggered) return;

        if (player == null) FindPlayer();
        if (player == null) return;
        if (TutorialManager.Instance == null) return;
        if (tutorialData == null) return;

        float dist = useXZOnly
            ? FlatDistance(transform.position, player.position)
            : Vector3.Distance(transform.position, player.position);

        bool inside = dist <= radius;

        if (inside && !_wasInside)
        {
            _triggered = true;
            Debug.Log("[TutorialZone] Player entered zone. Firing: " + tutorialData.name);
            TutorialManager.Instance.Show(tutorialData);
        }

        _wasInside = inside;
    }

    public void ResetZone()
    {
        _triggered = false;
        _wasInside = false;
    }

    private void FindPlayer()
    {
        GameObject go = GameObject.FindGameObjectWithTag(playerTag);
        if (go != null) player = go.transform;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
        Gizmos.DrawSphere(transform.position, radius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
