using UnityEngine;

public class SFXManager : MonoBehaviour
{
    public static SFXManager Instance { get; private set; }

    [Header("Rifle SFX")]
    public AudioClip playerRifleShot;
    public AudioClip enemyRifleShot;

    [Header("Footstep SFX")]
    public AudioClip[] playerFootsteps;   // array → randomized per step
    public AudioClip[] enemyFootsteps;

    [Header("Settings")]
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Range(0.85f, 1.15f)] public float pitchVariance = 0.1f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void PlayAtPoint(AudioClip clip, Vector3 position, float volumeScale = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, sfxVolume * volumeScale);
    }

    public void PlayRandomAtPoint(AudioClip[] clips, Vector3 position, float volumeScale = 1f)
    {
        if (clips == null || clips.Length == 0) return;
        AudioClip clip = clips[Random.Range(0, clips.Length)];
        PlayAtPoint(clip, position, volumeScale);
    }

    // Returns a randomized pitch value for variation
    public float RandomPitch() => Random.Range(1f - pitchVariance, 1f + pitchVariance);
}