public interface IDamageable
{
    void TakeDamage(float amount, UnityEngine.GameObject source = null);
    bool IsAlive { get; }
}