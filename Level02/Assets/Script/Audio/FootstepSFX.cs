using UnityEngine;

public class FootstepSFX : MonoBehaviour
{
    public enum FootstepOwner { Player, Enemy }
    public FootstepOwner owner = FootstepOwner.Player;

    [Range(0f, 1f)] public float volumeScale = 0.6f;

    private AudioSource _source;

    void Awake()
    {
        _source = gameObject.AddComponent<AudioSource>();
        _source.spatialBlend = 1f;
        _source.rolloffMode  = AudioRolloffMode.Linear;
        _source.maxDistance  = 20f;
        _source.playOnAwake  = false;
    }

    // Called by Animation Event "OnFootstep" on Mannequin_Man animator
    public void OnFootstep(AnimationEvent animationEvent)
    {
        // Threshold prevents ghost triggers at blend-tree weight transitions
        if (animationEvent.animatorClipInfo.weight < 0.5f) return;

        if (SFXManager.Instance == null) return;

        AudioClip[] pool = owner == FootstepOwner.Player
            ? SFXManager.Instance.playerFootsteps
            : SFXManager.Instance.enemyFootsteps;

        if (pool == null || pool.Length == 0) return;

        _source.clip   = pool[Random.Range(0, pool.Length)];
        _source.pitch  = SFXManager.Instance.RandomPitch();
        _source.volume = SFXManager.Instance.sfxVolume * volumeScale;
        _source.Play();
    }
}