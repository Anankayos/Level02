using UnityEngine;
using UnityEngine.Events;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    private float _currentHealth;

    // UI can subscribe to this without touching this script
    public UnityEvent<float, float> OnHealthChanged; // (current, max)
    public UnityEvent OnPlayerDied;

    private void Start()
    {
        _currentHealth = maxHealth;
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
    }

    public void Heal(float amount)
    {
        float before = _currentHealth;
        _currentHealth = Mathf.Clamp(_currentHealth + amount, 0f, maxHealth);
        Debug.Log($"[Health] +{_currentHealth - before} HP → {_currentHealth}/{maxHealth}");
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
    }
    public bool IsAlive => _currentHealth > 0f;

    public void SetHealth(float value)
    {
        _currentHealth = Mathf.Clamp(value, 0f, maxHealth);
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
    }

    public void TakeDamage(float amount)
    {
        _currentHealth = Mathf.Clamp(_currentHealth - amount, 0f, maxHealth);
        OnHealthChanged?.Invoke(_currentHealth, maxHealth);
        if (_currentHealth <= 0f) OnPlayerDied?.Invoke();
    }

    public float CurrentHealth => _currentHealth;
    public float MaxHealth     => maxHealth;
}