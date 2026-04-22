using UnityEngine;
using System;

public class BossPart : MonoBehaviour, IDamageable
{
    [Header("Part Config")]
    [SerializeField] private string partName = "Chest Plate";
    [SerializeField] private float partHealth = 50f;
    [SerializeField] private GameObject destroyedVFX;
    [SerializeField] private GameObject destroyedMesh;
    [SerializeField] private GameObject intactMesh;
    
    [Header("Visuals")]
    [SerializeField] private MeshRenderer partRenderer;
    [SerializeField] private Color targetColor = Color.red;
    
    private Color _originalColor;

    public bool IsDestroyed { get; private set; } = false;
    public static event Action<BossPart> OnPartDestroyed;

    private float _currentHealth;

    private void Awake() 
    {
        _currentHealth = partHealth;
        if (partRenderer != null)
            _originalColor = partRenderer.material.color;
    }

    public bool IsAlive => !IsDestroyed;

    public void TakeDamage(float amount, GameObject source = null)
    {
        if (IsDestroyed) return;
        
        _currentHealth -= amount;
        Debug.Log($"[BossPart] {partName} hit for {amount}. HP: {_currentHealth}");

        if (_currentHealth <= 0)
            DestroyPart();
    }

    private void DestroyPart()
    {
        IsDestroyed = true;

        if (destroyedVFX) Instantiate(destroyedVFX, transform.position, Quaternion.identity);
        if (intactMesh)    intactMesh.SetActive(false);
        if (destroyedMesh) destroyedMesh.SetActive(true);
        if (TryGetComponent<Collider>(out var col)) col.enabled = false;

        // Turn invisible when destroyed
        if (partRenderer != null)
            partRenderer.enabled = false;

        OnPartDestroyed?.Invoke(this);
    }

    public void HighlightPart()
    {
        if (partRenderer != null && !IsDestroyed)
            partRenderer.material.color = targetColor;
    }

    public void ResetPart()
    {
        IsDestroyed = false;
        _currentHealth = partHealth;
        
        if (intactMesh) intactMesh.SetActive(true);
        if (destroyedMesh) destroyedMesh.SetActive(false);
        if (TryGetComponent<Collider>(out var col)) col.enabled = true;
        
        // Make visible again and reset color
        if (partRenderer != null)
        {
            partRenderer.enabled = true;
            partRenderer.material.color = _originalColor;
        }
    }
}