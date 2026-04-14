using UnityEngine;

// The Walk_N animation event calls OnFootstep() automatically on each step.
public class EnemyFootstepHandler : MonoBehaviour
{
    [SerializeField] private float walkNoiseRadius = 3f;
    [SerializeField] private AudioClip[] footstepSounds;  // optional, drag clips in Inspector
    private AudioSource _audio;

    private void Awake() => _audio = GetComponent<AudioSource>();

    // Called automatically by the Animation Event on each footstep
   private void OnFootstep()
{
    // Pass gameObject so this enemy (and others) can identify the source
    NoiseEmitter.EmitNoise(transform.position, walkNoiseRadius,
                           NoiseType.Footstep, gameObject);

    if (_audio != null && footstepSounds.Length > 0)
        _audio.PlayOneShot(footstepSounds[Random.Range(0, footstepSounds.Length)]);
}
    }
