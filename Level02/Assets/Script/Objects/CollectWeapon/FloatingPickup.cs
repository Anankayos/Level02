using UnityEngine;

public abstract class FloatingPickup : MonoBehaviour, IResettable
{
    [Header("Float Animation")]
    [SerializeField] private float floatAmplitude = 0.25f;
    [SerializeField] private float floatFrequency = 1.5f;
    [SerializeField] private float rotationSpeed  = 60f;
    [SerializeField] private float phaseOffset    = 0f;

    [Header("Detection")]
    [SerializeField] private string playerTag = "Player";

    private Vector3 _originPosition;

    protected virtual void Start()
    {
        _originPosition = transform.position;
        phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void Update()
    {
        float newY = _originPosition.y
                   + Mathf.Sin((Time.time * floatFrequency) + phaseOffset) * floatAmplitude;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;
        OnPickedUp(other.gameObject);
    }

    // ── IResettable ───────────────────────────────────────────
    // Stable scene-path ID — matches across play sessions so
    // checkpoint save/load can correctly suppress or restore this pickup.
    public string ResettableID => GetScenePath();

    private string GetScenePath()
    {
        var t = transform;
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return "FP:" + path;
    }

    public virtual void SaveInitialState() { }

    public virtual void ResetState()
    {
        gameObject.SetActive(true);
    }

    // ─────────────────────────────────────────────────────────

    protected abstract void OnPickedUp(GameObject player);

    protected void DestroyPickup()
    {
        SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);
        gameObject.SetActive(false);
    }
}
