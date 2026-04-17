using UnityEngine;
using System;

public class BossPart : MonoBehaviour, IDamageable
{
    [Header("Part Config")]
    [SerializeField] private string partName = "Chest Plate";
    [SerializeField] private float partHealth = 50f;
    [SerializeField] private GameObject destroyedVFX;       // particle prefab
    [SerializeField] private GameObject destroyedMesh;      // optional broken mesh
    [SerializeField] private GameObject intactMesh;

    public bool IsDestroyed { get; private set; } = false;
    public static event Action<BossPart> OnPartDestroyed;

    private float _currentHealth;

    private void Awake() => _currentHealth = partHealth;

    // IDamageable implementation — matches your project's signature
    public bool IsAlive => !IsDestroyed;

public void TakeDamage(float amount, GameObject source = null)
{
    if (IsDestroyed) return;

    _currentHealth -= amount;
    GameEvents.FireHit(HitType.Destructible);

    if (_currentHealth <= 0f) DestroyPart();
}
    private void DestroyPart()
    {
        IsDestroyed = true;

        if (destroyedVFX) Instantiate(destroyedVFX, transform.position, Quaternion.identity);
        if (intactMesh) intactMesh.SetActive(false);
        if (destroyedMesh) destroyedMesh.SetActive(true);

        // Disable collider so bullets pass through
        if (TryGetComponent<Collider>(out var col)) col.enabled = false;

        OnPartDestroyed?.Invoke(this);
        Debug.Log($"[Boss] Part destroyed: {partName}");
    }

    // Called by CheckpointManager / IResettable on boss reset
    public void ResetPart()
    {
        IsDestroyed = false;
        _currentHealth = partHealth;
        if (intactMesh) intactMesh.SetActive(true);
        if (destroyedMesh) destroyedMesh.SetActive(false);
        if (TryGetComponent<Collider>(out var col)) col.enabled = true;
    }
}