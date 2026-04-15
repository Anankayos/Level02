using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

public class CameraAimController : MonoBehaviour
{
    [Header("Camera Reference")]
    [Tooltip("Your main PlayerFollowCamera CinemachineCamera.")]
    public CinemachineCamera virtualCamera;

    // ── Normal State ──────────────────────────────────────────
    [Header("Normal Camera")]
    public float normalFOV              = 70f;
    public float normalCameraDistance  = 3.5f;
    public Vector3 normalShoulderOffset = new Vector3(0.3f, 0.2f, 0f);
    public float normalVerticalArm     = 0.5f;
    [Range(0f, 1f)]
    public float normalCameraSide      = 0.5f;   // centered

    // ── Aim State (Shadow of the Tomb Raider) ─────────────────
    [Header("Aim Camera — SOTTR Style")]
    [Tooltip("Tighten FOV slightly while aiming.")]
    public float aimFOV               = 58f;
    [Tooltip("Closer to the character while aiming.")]
    public float aimCameraDistance    = 1.8f;
    [Tooltip("Shift right to see over the shoulder. X=0.6 gives SOTTR feel.")]
    public Vector3 aimShoulderOffset  = new Vector3(0.65f, 0.18f, 0f);
    [Tooltip("Lower arm so camera sits at shoulder height.")]
    public float aimVerticalArm       = 0.25f;
    [Range(0f, 1f)]
    [Tooltip("1 = fully right shoulder. Matches SOTTR.")]
    public float aimCameraSide        = 1f;

    // ── Transition ────────────────────────────────────────────
    [Header("Blend Speed")]
    [Tooltip("Seconds to fully transition into aim mode.")]
    public float aimInTime  = 0.18f;
    [Tooltip("Seconds to fully transition out of aim mode.")]
    public float aimOutTime = 0.22f;

    // ── Runtime ───────────────────────────────────────────────
    private CinemachineThirdPersonFollow _follow;
    private Coroutine _blendRoutine;
    private bool _isAiming;

    // ─────────────────────────────────────────────────────────
    void Awake()
    {
        if (virtualCamera == null)
        {
            Debug.LogError("[CameraAimController] virtualCamera not assigned.");
            return;
        }
        _follow = virtualCamera.GetComponent<CinemachineThirdPersonFollow>();
        if (_follow == null)
            Debug.LogError("[CameraAimController] No CinemachineThirdPersonFollow on camera.");

        // Apply normal state immediately
        ApplyState(0f);
    }

    // ─────────────────────────────────────────────────────────
    // PUBLIC API — called by PlayerCombat.SetAimMode()
    // ─────────────────────────────────────────────────────────

    public void SetAim(bool aim, bool instant = false)
    {
        if (_isAiming == aim) return;
        _isAiming = aim;

        if (_blendRoutine != null) StopCoroutine(_blendRoutine);

        if (instant)
        {
            ApplyState(aim ? 1f : 0f);
        }
        else
        {
            float duration = aim ? aimInTime : aimOutTime;
            _blendRoutine  = StartCoroutine(BlendRoutine(aim, duration));
        }
    }

    // ─────────────────────────────────────────────────────────
    IEnumerator BlendRoutine(bool toAim, float duration)
    {
        float elapsed = 0f;

        // Get current live values as the starting point
        float startFOV      = virtualCamera.Lens.FieldOfView;
        float startDist     = _follow != null ? _follow.CameraDistance : (toAim ? normalCameraDistance : aimCameraDistance);
        Vector3 startShould = _follow != null ? _follow.ShoulderOffset : normalShoulderOffset;
        float startVArm     = _follow != null ? _follow.VerticalArmLength : normalVerticalArm;
        float startSide     = _follow != null ? _follow.CameraSide : normalCameraSide;

        float targetFOV      = toAim ? aimFOV      : normalFOV;
        float targetDist     = toAim ? aimCameraDistance : normalCameraDistance;
        Vector3 targetShould = toAim ? aimShoulderOffset : normalShoulderOffset;
        float targetVArm     = toAim ? aimVerticalArm    : normalVerticalArm;
        float targetSide     = toAim ? aimCameraSide     : normalCameraSide;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // Smooth step for a polished SOTTR-like feel
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            ApplyLerped(t, startFOV, targetFOV,
                           startDist, targetDist,
                           startShould, targetShould,
                           startVArm, targetVArm,
                           startSide, targetSide);
            yield return null;
        }

        ApplyState(toAim ? 1f : 0f);
    }

    void ApplyLerped(float t,
        float sFOV, float tFOV,
        float sDist, float tDist,
        Vector3 sShould, Vector3 tShould,
        float sVArm, float tVArm,
        float sSide, float tSide)
    {
        var lens = virtualCamera.Lens;
        lens.FieldOfView = Mathf.Lerp(sFOV, tFOV, t);
        virtualCamera.Lens = lens;

        if (_follow == null) return;
        _follow.CameraDistance    = Mathf.Lerp(sDist,    tDist,    t);
        _follow.ShoulderOffset    = Vector3.Lerp(sShould, tShould,  t);
        _follow.VerticalArmLength = Mathf.Lerp(sVArm,   tVArm,   t);
        _follow.CameraSide        = Mathf.Lerp(sSide,   tSide,   t);
    }

    void ApplyState(float t)
    {
        ApplyLerped(t,
            normalFOV,            aimFOV,
            normalCameraDistance, aimCameraDistance,
            normalShoulderOffset, aimShoulderOffset,
            normalVerticalArm,    aimVerticalArm,
            normalCameraSide,     aimCameraSide);
    }
}