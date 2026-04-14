using UnityEngine;

public class MedikitPickup : FloatingPickup
{
    [Header("Medikit")]
    [SerializeField] private float healAmount = 30f;
    [SerializeField] private bool  skipIfFull = true; // leave on ground if player is healthy

    protected override void OnPickedUp(GameObject player)
    {
        var health = player.GetComponent<PlayerHealth>();
        if (health == null) return;

        if (skipIfFull && health.CurrentHealth >= health.MaxHealth)
        {
            Debug.Log("[Medikit] Player is already full HP, not consumed.");
            return;
        }

        health.Heal(healAmount);
        DestroyPickup();
    }
}