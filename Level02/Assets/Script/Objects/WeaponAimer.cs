using UnityEngine;

public class WeaponAimer : MonoBehaviour
{
    [Header("References (auto-assigned by WeaponPickup)")]
    public Transform handBone;
    public Transform playerRoot;   // used to convert holdOffset to player-local space
    public Camera    aimCamera;
    public LayerMask aimMask = ~0;

    [Header("Position — player-local offset from hand bone")]
    [Tooltip("Offset in PLAYER space (not world, not hand-bone space).\n" +
             "X = right/left of player body\n" +
             "Y = up/down\n" +
             "Z = forward/back along player facing\n" +
             "Tweak at runtime until the grip sits in the hand.")]
    public Vector3 holdOffset = new Vector3(0.1f, 0f, 0.15f);

    [Header("Rotation — model correction")]
    [Tooltip("Applied AFTER LookRotation so you can fix the weapon model axis.\n" +
             "If the barrel points sideways, rotate Y ±90.\n" +
             "If it points up/down, rotate X ±90.")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("Aim Settings")]
    public float aimDistance  = 200f;
    [Tooltip("0 = instant snap. >0 = smooth follow (good for recoil feel).")]
    public float rotationSpeed = 0f;

    void LateUpdate()
    {
        if (handBone == null || aimCamera == null) return;

        // ── Position: hand + offset in PLAYER-LOCAL space ─────
        // TransformDirection converts the offset from player-local → world,
        // so the weapon stays fixed relative to the body as the camera rotates.
        Vector3 worldOffset = playerRoot != null
            ? playerRoot.TransformDirection(holdOffset)
            : holdOffset;

        transform.position = handBone.position + worldOffset;

        // ── Rotation: aim at screen center + correction ───────
        Ray ray = aimCamera.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        Vector3 worldTarget = Physics.Raycast(ray, out RaycastHit hit, aimDistance, aimMask)
            ? hit.point
            : ray.GetPoint(aimDistance);

        Vector3 dir = (worldTarget - transform.position).normalized;
        if (dir == Vector3.zero) return;

        // Base aim rotation + local model correction
        Quaternion aimRot       = Quaternion.LookRotation(dir);
        Quaternion corrected    = aimRot * Quaternion.Euler(rotationOffset);

        transform.rotation = rotationSpeed <= 0f
            ? corrected
            : Quaternion.Slerp(transform.rotation, corrected, rotationSpeed * Time.deltaTime);
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        // Yellow line = barrel direction
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 3f);
        // Cyan sphere = weapon pivot
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.04f);
    }
#endif
}