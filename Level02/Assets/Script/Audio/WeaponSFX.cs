using UnityEngine;

public class WeaponSFX : MonoBehaviour
{
    public enum ShooterType { Player, Enemy }

    [Header("Settings")]
    public ShooterType shooterType = ShooterType.Enemy;
    
    [Tooltip("Check this to play the sound instantly when this object spawns (use for projectiles)")]
    public bool playOnStart = true; 
    
    [Tooltip("Check this for the Player to make the gunshot always loud, ignoring camera distance")]
    public bool force2DSound = false;

    [Range(0f, 1f)]
    public float volumeScale = 1f;

    private AudioSource _source;

    void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        
        // If force2DSound is true, spatialBlend is 0 (always audible). Otherwise 1 (3D sound)
        _source.spatialBlend = force2DSound ? 0f : 1f; 
        _source.rolloffMode  = AudioRolloffMode.Linear;
        _source.minDistance  = 10f;  // Sound stays at max volume within 10 units
        _source.maxDistance  = 100f; // Increased from 30 to 100 so it doesn't fade out too fast
        _source.playOnAwake  = false;
    }

    void Start()
    {
        if (playOnStart)
        {
            PlayShot();
        }
    }

    public void PlayShot()
    {
        if (SFXManager.Instance == null) return;

        AudioClip clip = shooterType == ShooterType.Player
            ? SFXManager.Instance.playerRifleShot
            : SFXManager.Instance.enemyRifleShot;

        if (clip == null) return;

        _source.pitch  = SFXManager.Instance.RandomPitch();
        _source.volume = SFXManager.Instance.sfxVolume * volumeScale;
        _source.clip   = clip;
        _source.Play();
    }
}