using UnityEngine;

// Attach alongside CharacterController to emit footstep noise automatically
[RequireComponent(typeof(CharacterController))]
public class PlayerNoiseEmitter : MonoBehaviour
{
    [SerializeField] private float walkNoiseRadius    = 4f;
    [SerializeField] private float runNoiseRadius     = 9f;
    [SerializeField] private float runSpeedThreshold  = 4f;
    [SerializeField] private float stepInterval       = 0.45f;

    private CharacterController _cc;
    private float               _stepTimer;

    private void Awake() => _cc = GetComponent<CharacterController>();

    private void Update()
    {
        if (_cc.velocity.magnitude < 0.1f) return;

        _stepTimer -= Time.deltaTime;
        if (_stepTimer > 0f) return;

        bool isRunning = _cc.velocity.magnitude >= runSpeedThreshold;
        NoiseEmitter.EmitNoise(
            transform.position,
            isRunning ? runNoiseRadius : walkNoiseRadius,
            NoiseType.Footstep
        );
        _stepTimer = stepInterval;
    }
}