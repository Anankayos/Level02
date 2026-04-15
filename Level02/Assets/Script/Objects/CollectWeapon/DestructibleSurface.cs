using UnityEngine;

[RequireComponent(typeof(Collider))]
public class DestructibleSurface : MonoBehaviour, IDamageable, IResettable
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 50f;

    [Header("Destruction")]
    [SerializeField] private GameObject destroyedVersionPrefab;  // optional rubble mesh
    [SerializeField] private GameObject destructionVFX;
    [SerializeField] private float      impactNoiseRadius = 6f;

    private float      _health;
    private bool       _isDestroyed;
    private string     _id;
    private GameObject _spawnedRubble;

    public string ResettableID =>
        string.IsNullOrEmpty(_id) ? (_id = System.Guid.NewGuid().ToString()) : _id;

    public bool IsAlive => !_isDestroyed;

    private void Awake()
    {
        _health = maxHealth;
        SaveInitialState();
    }

    public void TakeDamage(float amount, GameObject source = null)
    {
        if (_isDestroyed) return;
        _health -= amount;
        NoiseEmitter.EmitNoise(transform.position, impactNoiseRadius, NoiseType.Impact);
        if (_health <= 0f) BreakApart(spawnVFX: true);
    }

    // Called silently on checkpoint restore — no VFX
    public void ForceDestroy() => BreakApart(spawnVFX: false);

    private void BreakApart(bool spawnVFX)
    {
        _isDestroyed = true;
        SceneStateTracker.Instance?.RegisterDestroyed(ResettableID);

        if (spawnVFX && destructionVFX)
            Instantiate(destructionVFX, transform.position, transform.rotation);

        if (destroyedVersionPrefab)
        {
            _spawnedRubble = Instantiate(
                destroyedVersionPrefab, transform.position, transform.rotation
            );
        }
        gameObject.SetActive(false);
    }

    public void SaveInitialState() { }

    public void ResetState()
    {
        _isDestroyed = false;
        _health      = maxHealth;
        if (_spawnedRubble) { Destroy(_spawnedRubble); _spawnedRubble = null; }
        gameObject.SetActive(true);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.position, GetComponent<Renderer>()?.bounds.size ?? Vector3.one);
    }
}