using UnityEngine;

[RequireComponent(typeof(PlayerInventory))]
public class PlayerShoot : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;

    [Header("Rifle Stats")]
    [SerializeField] private float range       = 100f;
    [SerializeField] private float damage      = 25f;
    [SerializeField] private float fireRate    = 0.12f;

    [Header("Noise")]
    [SerializeField] private float gunshotNoiseRadius = 25f;

    private PlayerInventory _inventory;
    private float           _nextFireTime;

    private void Awake() => _inventory = GetComponent<PlayerInventory>();

    private void Update()
    {
        if (!_inventory.HasRifle) return;
        if (Input.GetButton("Fire1") && Time.time >= _nextFireTime)
            Shoot();
    }

    private void Shoot()
    {
        if (_inventory.Ammo <= 0) { Debug.Log("[Shoot] No ammo!"); return; }

        _nextFireTime = Time.time + fireRate;
        _inventory.UseAmmo(1);

        // Broadcast noise — enemies within radius will react
        NoiseEmitter.EmitNoise(transform.position, gunshotNoiseRadius, NoiseType.Gunshot);

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f)
        );

        if (Physics.Raycast(ray, out RaycastHit hit, range))
        {
            var damageable = hit.collider.GetComponent<IDamageable>();
            damageable?.TakeDamage(damage, gameObject);
        }
    }
}