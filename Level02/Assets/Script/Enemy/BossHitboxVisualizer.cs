using UnityEngine;
using System.Collections;

/// <summary>
/// BossHitboxVisualizer — Horizon Zero Dawn-style hitbox feedback without shaders or textures.
///
/// HOW IT WORKS (no shaders needed):
///   1. Each BossPart gets a secondary "overlay" MeshFilter+MeshRenderer child object.
///   2. The overlay uses a BUILT-IN transparent/additive material (no asset required).
///   3. On hit, the overlay pulses from fully opaque down to 0 alpha using a Coroutine.
///   4. Color encodes hit severity: green → yellow → orange → red as HP% drops.
///   5. A thin wireframe effect is faked via slightly scaled overlay + inverted normals trick.
///   6. gizmos in the editor show hitbox volumes at all times (editor-only, zero runtime cost).
///
/// SETUP:
///   - Add this component to the SAME GameObject that has BossPart.
///   - Assign [overlayMesh] (usually the same mesh as the visible part, or a capsule/box approximation).
///   - The script auto-creates a child overlay object at runtime.
///   - Call NotifyHit(damage, currentHP, maxHP) from BossPart.TakeDamage after each hit.
/// </summary>
[RequireComponent(typeof(BossPart))]
public class BossHitboxVisualizer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Overlay Mesh")]
    [Tooltip("The mesh used for the hit overlay. Usually the same mesh as the visible part.")]
    [SerializeField] private Mesh overlayMesh;
    [Tooltip("Uniform scale applied to the overlay on top of the part's scale. Slightly > 1 ensures z-fight-free overlap.")]
    [SerializeField] private float overlayScaleBias = 1.02f;

    [Header("Pulse Settings")]
    [Tooltip("Peak alpha of the overlay flash at the moment of impact.")]
    [SerializeField] [Range(0f, 1f)] private float peakAlpha = 0.75f;
    [Tooltip("How long (seconds) the flash takes to fade out.")]
    [SerializeField] private float fadeDuration = 0.35f;
    [Tooltip("Secondary slower glow that lingers after the flash.")]
    [SerializeField] [Range(0f, 0.5f)] private float lingerAlpha = 0.18f;
    [Tooltip("How long the linger glow stays before fading fully.")]
    [SerializeField] private float lingerDuration = 1.2f;

    [Header("Color Encoding (HP % thresholds)")]
    [Tooltip("Color when the part is at full/high health.")]
    [SerializeField] private Color colorHealthy    = new Color(0.2f, 0.9f, 0.4f, 1f);   // green
    [Tooltip("Color when the part HP drops below midThreshold.")]
    [SerializeField] private Color colorDamaged    = new Color(1.0f, 0.8f, 0.1f, 1f);   // yellow
    [Tooltip("Color when the part HP drops below lowThreshold.")]
    [SerializeField] private Color colorCritical   = new Color(1.0f, 0.3f, 0.05f, 1f);  // orange-red
    [Tooltip("Color when the part is about to be destroyed.")]
    [SerializeField] private Color colorDestroyed  = new Color(1.0f, 0.05f, 0.05f, 1f); // deep red

    [SerializeField] [Range(0f, 1f)] private float midThreshold  = 0.6f;
    [SerializeField] [Range(0f, 1f)] private float lowThreshold  = 0.3f;
    [SerializeField] [Range(0f, 1f)] private float critThreshold = 0.10f;

    [Header("Scan Line Effect (HZD flavour)")]
    [Tooltip("Enable a second slightly-larger wireframe-ish overlay that mimics HZD's scan rim.")]
    [SerializeField] private bool enableRimOverlay = true;
    [SerializeField] [Range(1.01f, 1.15f)] private float rimScaleBias = 1.06f;
    [SerializeField] [Range(0f, 1f)] private float rimAlphaMultiplier = 0.4f;

    [Header("Debug / Editor")]
    [Tooltip("Show hitbox gizmos in Scene view even when not selected.")]
    [SerializeField] private bool alwaysShowGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0.5f, 0.25f);

    // ─── Private ──────────────────────────────────────────────────────────────
    private MeshRenderer _overlayRenderer;
    private MeshRenderer _rimRenderer;
    private MaterialPropertyBlock _propBlock;
    private MaterialPropertyBlock _rimPropBlock;

    private Coroutine _flashCoroutine;
    private Coroutine _lingerCoroutine;

    private static Material _sharedOverlayMat;
    private static Material _sharedAdditiveMat;

    // ─── Unity ────────────────────────────────────────────────────────────────
    private void Awake()
    {
        EnsureSharedMaterials();
        BuildOverlayObjects();
        _propBlock    = new MaterialPropertyBlock();
        _rimPropBlock = new MaterialPropertyBlock();
        SetOverlayAlpha(0f, Color.white, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            SetOverlayAlpha(0f, Color.white, _rimRenderer, _rimPropBlock);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Call this from BossPart.TakeDamage to trigger the visual feedback.
    /// </summary>
    /// <param name="damage">Damage amount dealt this hit (used only for debug).</param>
    /// <param name="currentHP">Current HP of the part after the hit.</param>
    /// <param name="maxHP">Maximum HP of the part.</param>
    public void NotifyHit(float damage, float currentHP, float maxHP)
    {
        float ratio = Mathf.Clamp01(currentHP / Mathf.Max(maxHP, 0.001f));
        Color hitColor = HPRatioToColor(ratio);

        if (_flashCoroutine  != null) StopCoroutine(_flashCoroutine);
        if (_lingerCoroutine != null) StopCoroutine(_lingerCoroutine);

        _flashCoroutine  = StartCoroutine(FlashPulse(hitColor));
        _lingerCoroutine = StartCoroutine(LingerGlow(hitColor));
    }

    /// <summary>
    /// Force-show the overlay at a given HP ratio permanently (e.g. for debug or scan mode).
    /// </summary>
    public void ShowStatic(float hpRatio)
    {
        Color c = HPRatioToColor(hpRatio);
        SetOverlayAlpha(lingerAlpha, c, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            SetOverlayAlpha(lingerAlpha * rimAlphaMultiplier, c, _rimRenderer, _rimPropBlock);
    }

    /// <summary>
    /// Hide all overlays immediately.
    /// </summary>
    public void Hide()
    {
        if (_flashCoroutine  != null) StopCoroutine(_flashCoroutine);
        if (_lingerCoroutine != null) StopCoroutine(_lingerCoroutine);
        SetOverlayAlpha(0f, Color.white, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            SetOverlayAlpha(0f, Color.white, _rimRenderer, _rimPropBlock);
    }

    // ─── Coroutines ───────────────────────────────────────────────────────────

    private IEnumerator FlashPulse(Color hitColor)
    {
        // Instant snap to peak alpha
        SetOverlayAlpha(peakAlpha, hitColor, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            SetOverlayAlpha(peakAlpha * rimAlphaMultiplier, hitColor, _rimRenderer, _rimPropBlock);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / fadeDuration;
            // Ease-out cubic: starts fast, decelerates
            float easedT = 1f - Mathf.Pow(1f - t, 3f);
            float alpha = Mathf.Lerp(peakAlpha, lingerAlpha, easedT);

            SetOverlayAlpha(alpha, hitColor, _overlayRenderer, _propBlock);
            if (_rimRenderer != null)
                SetOverlayAlpha(alpha * rimAlphaMultiplier, hitColor, _rimRenderer, _rimPropBlock);
            yield return null;
        }

        _flashCoroutine = null;
    }

    private IEnumerator LingerGlow(Color hitColor)
    {
        // Wait for flash to finish first
        yield return new WaitForSeconds(fadeDuration);

        // Hold linger
        float holdTime = lingerDuration * 0.5f;
        yield return new WaitForSeconds(holdTime);

        // Fade out linger
        float elapsed = 0f;
        float remainingFade = lingerDuration * 0.5f;
        while (elapsed < remainingFade)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(lingerAlpha, 0f, elapsed / remainingFade);
            SetOverlayAlpha(alpha, hitColor, _overlayRenderer, _propBlock);
            if (_rimRenderer != null)
                SetOverlayAlpha(alpha * rimAlphaMultiplier, hitColor, _rimRenderer, _rimPropBlock);
            yield return null;
        }

        SetOverlayAlpha(0f, hitColor, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            SetOverlayAlpha(0f, hitColor, _rimRenderer, _rimPropBlock);

        _lingerCoroutine = null;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private Color HPRatioToColor(float ratio)
    {
        if (ratio > midThreshold)
            return Color.Lerp(colorDamaged,  colorHealthy, (ratio - midThreshold) / (1f - midThreshold));
        if (ratio > lowThreshold)
            return Color.Lerp(colorCritical, colorDamaged, (ratio - lowThreshold)  / (midThreshold  - lowThreshold));
        if (ratio > critThreshold)
            return Color.Lerp(colorDestroyed, colorCritical, (ratio - critThreshold) / (lowThreshold - critThreshold));
        return colorDestroyed;
    }

    private void SetOverlayAlpha(float alpha, Color baseColor, MeshRenderer renderer, MaterialPropertyBlock block)
    {
        if (renderer == null) return;
        Color c = baseColor;
        c.a = alpha;
        block.SetColor("_Color", c);
        renderer.SetPropertyBlock(block);
        // Disable the GO entirely when invisible to save draw calls
        renderer.gameObject.SetActive(alpha > 0.005f);
    }

    private void BuildOverlayObjects()
    {
        if (overlayMesh == null)
        {
            // Fallback: try to grab mesh from sibling MeshFilter
            var mf = GetComponent<MeshFilter>();
            if (mf == null) mf = GetComponentInChildren<MeshFilter>();
            if (mf != null) overlayMesh = mf.sharedMesh;
        }

        if (overlayMesh == null)
        {
            Debug.LogWarning($"[BossHitboxVisualizer] No overlay mesh found on {name}. Assign one in the Inspector.");
            return;
        }

        _overlayRenderer = CreateOverlayChild("_HitOverlay", overlayScaleBias, _sharedOverlayMat);

        if (enableRimOverlay)
            _rimRenderer = CreateOverlayChild("_HitRim", rimScaleBias, _sharedAdditiveMat);
    }

    private MeshRenderer CreateOverlayChild(string childName, float scaleBias, Material mat)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one * scaleBias;
        go.SetActive(false);

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = overlayMesh;

        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows     = false;
        mr.lightProbeUsage    = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        return mr;
    }

    /// <summary>
    /// Ensures the two shared materials exist exactly once across all BossHitboxVisualizer instances.
    /// Uses only Unity built-in shaders — no custom shader or texture asset required.
    /// </summary>
    private static void EnsureSharedMaterials()
    {
        if (_sharedOverlayMat == null)
        {
            // Standard transparent material — color set per-frame via MaterialPropertyBlock
            _sharedOverlayMat = new Material(Shader.Find("Standard"));
            _sharedOverlayMat.name = "BossHitbox_Overlay";
            // Configure for transparency
            _sharedOverlayMat.SetFloat("_Mode", 3);                         // Transparent blend mode
            _sharedOverlayMat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _sharedOverlayMat.SetInt("_DstBlend",  (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _sharedOverlayMat.SetInt("_ZWrite",    0);
            _sharedOverlayMat.DisableKeyword("_ALPHATEST_ON");
            _sharedOverlayMat.EnableKeyword("_ALPHABLEND_ON");
            _sharedOverlayMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _sharedOverlayMat.renderQueue = 3000;
            _sharedOverlayMat.color = Color.white;
        }

        if (_sharedAdditiveMat == null)
        {
            // Additive material for the rim glow — creates a "bloom" impression without post-processing
            _sharedAdditiveMat = new Material(Shader.Find("Particles/Additive"));
            if (_sharedAdditiveMat.shader == null || !_sharedAdditiveMat.shader.isSupported)
            {
                // Fallback if Particles/Additive not available (URP/HDRP projects)
                _sharedAdditiveMat = new Material(Shader.Find("Standard"));
                _sharedAdditiveMat.SetFloat("_Mode", 3);
                _sharedAdditiveMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _sharedAdditiveMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);  // Additive
                _sharedAdditiveMat.SetInt("_ZWrite",   0);
                _sharedAdditiveMat.renderQueue = 3001;
            }
            _sharedAdditiveMat.name = "BossHitbox_Rim";
            _sharedAdditiveMat.color = Color.white;
        }
    }

    // ─── Editor Gizmos ────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (alwaysShowGizmo) DrawHitboxGizmo();
    }

    private void OnDrawGizmosSelected()
    {
        if (!alwaysShowGizmo) DrawHitboxGizmo();
    }

    private void DrawHitboxGizmo()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = gizmoColor;

        switch (col)
        {
            case BoxCollider box:
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
                Gizmos.DrawWireCube(box.center, box.size);
                break;

            case SphereCollider sphere:
                float worldRadius = sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
                Gizmos.DrawSphere(transform.TransformPoint(sphere.center), worldRadius);
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
                Gizmos.DrawWireSphere(transform.TransformPoint(sphere.center), worldRadius);
                break;

            case CapsuleCollider capsule:
                // Unity has no built-in Gizmos.DrawCapsule; approximate with sphere + wire
                Vector3 worldCenter = transform.TransformPoint(capsule.center);
                float wRadius = capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                Gizmos.DrawSphere(worldCenter, wRadius);
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
                Gizmos.DrawWireSphere(worldCenter, wRadius);
                break;

            default:
                // Generic: just draw a wire sphere at part pivot
                Gizmos.DrawWireSphere(transform.position, 0.3f);
                break;
        }
    }
#endif
}
