using UnityEngine;
using System.Collections;

/// <summary>
/// BossHitboxVisualizer — HZD-style hitbox feedback, URP/HDRP/Built-in safe.
///
/// DEBUG VERSION: every critical step logs to the Console so you can trace exactly
/// where the pipeline breaks.  Filter Console by "[BHV]" to see only these messages.
///
/// SETUP:
///   1. Add this component to the same GameObject as BossPart.
///   2. Optionally assign Overlay Mesh in Inspector — if empty it auto-finds MeshFilter.
///   3. Hit Play and shoot the boss — watch the Console for [BHV] lines.
/// </summary>
[RequireComponent(typeof(BossPart))]
public class BossHitboxVisualizer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Overlay Mesh")]
    [Tooltip("Mesh used for the hit overlay. Leave empty to auto-detect from MeshFilter.")]
    [SerializeField] private Mesh overlayMesh;
    [SerializeField] private float overlayScaleBias = 1.02f;

    [Header("Pulse Settings")]
    [SerializeField] [Range(0f, 1f)] private float peakAlpha     = 0.75f;
    [SerializeField]                 private float fadeDuration   = 0.35f;
    [SerializeField] [Range(0f, 0.5f)] private float lingerAlpha  = 0.18f;
    [SerializeField]                 private float lingerDuration  = 1.2f;

    [Header("Color Encoding")]
    [SerializeField] private Color colorHealthy  = new Color(0.2f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color colorDamaged  = new Color(1.0f, 0.8f, 0.1f, 1f);
    [SerializeField] private Color colorCritical = new Color(1.0f, 0.3f, 0.05f, 1f);
    [SerializeField] private Color colorDestroyed= new Color(1.0f, 0.05f, 0.05f, 1f);
    [SerializeField] [Range(0f,1f)] private float midThreshold  = 0.6f;
    [SerializeField] [Range(0f,1f)] private float lowThreshold  = 0.3f;
    [SerializeField] [Range(0f,1f)] private float critThreshold = 0.10f;

    [Header("Rim Overlay")]
    [SerializeField] private bool  enableRimOverlay    = true;
    [SerializeField] [Range(1.01f, 1.15f)] private float rimScaleBias        = 1.06f;
    [SerializeField] [Range(0f,1f)]        private float rimAlphaMultiplier  = 0.4f;

    [Header("Debug / Editor")]
    [SerializeField] private bool  alwaysShowGizmo = true;
    [SerializeField] private Color gizmoColor      = new Color(0f, 1f, 0.5f, 0.25f);
    [Tooltip("Force the overlay to stay visible at this alpha permanently (0 = off). " +
             "Use in Play mode to verify the mesh/material are working before testing hits.")]
    [SerializeField] [Range(0f,1f)] private float debugForceAlpha = 0f;

    // ─── Private ──────────────────────────────────────────────────────────────
    private GameObject   _overlayGO;
    private GameObject   _rimGO;
    private MeshRenderer _overlayRenderer;
    private MeshRenderer _rimRenderer;
    private MaterialPropertyBlock _propBlock;
    private MaterialPropertyBlock _rimPropBlock;
    private Coroutine _flashCoroutine;
    private Coroutine _lingerCoroutine;
    private bool _ready = false;

    // Per-instance materials so URP/HDRP property changes don't bleed across objects
    private Material _overlayMat;
    private Material _rimMat;

    // ─── Unity ────────────────────────────────────────────────────────────────
    private void Awake()
    {
        Debug.Log($"[BHV] Awake on '{name}'");
        _propBlock    = new MaterialPropertyBlock();
        _rimPropBlock = new MaterialPropertyBlock();
        _ready = BuildMaterials() && BuildOverlayObjects();
        Debug.Log($"[BHV] Awake complete. _ready={_ready}");
    }

    private void Start()
    {
        if (!_ready)
        {
            Debug.LogError($"[BHV] '{name}' is NOT ready — overlay will not show. Check warnings above.");
            return;
        }

        // Ensure overlays start invisible
        ApplyAlpha(0f, Color.white, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            ApplyAlpha(0f, Color.white, _rimRenderer, _rimPropBlock);

        Debug.Log($"[BHV] '{name}' initialized OK. overlayGO='{_overlayGO?.name}'  rimGO='{_rimGO?.name}'");
    }

    private void Update()
    {
        // Debug knob: drag debugForceAlpha > 0 in Play mode to verify rendering
        if (debugForceAlpha > 0f && _ready)
        {
            Color c = HPRatioToColor(1f - debugForceAlpha);
            ApplyAlpha(debugForceAlpha, c, _overlayRenderer, _propBlock, forceActive: true);
            if (_rimRenderer != null)
                ApplyAlpha(debugForceAlpha * rimAlphaMultiplier, c, _rimRenderer, _rimPropBlock, forceActive: true);
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void NotifyHit(float damage, float currentHP, float maxHP)
    {
        if (!_ready)
        {
            Debug.LogWarning($"[BHV] NotifyHit on '{name}' but _ready=false — overlay skipped.");
            return;
        }

        float ratio    = Mathf.Clamp01(currentHP / Mathf.Max(maxHP, 0.001f));
        Color hitColor = HPRatioToColor(ratio);
        Debug.Log($"[BHV] NotifyHit '{name}' | dmg={damage} hp={currentHP}/{maxHP} ratio={ratio:F2} color={hitColor}");

        if (_flashCoroutine  != null) StopCoroutine(_flashCoroutine);
        if (_lingerCoroutine != null) StopCoroutine(_lingerCoroutine);
        _flashCoroutine  = StartCoroutine(FlashPulse(hitColor));
        _lingerCoroutine = StartCoroutine(LingerGlow(hitColor));
    }

    public void ShowStatic(float hpRatio)
    {
        if (!_ready) return;
        Color c = HPRatioToColor(hpRatio);
        ApplyAlpha(lingerAlpha, c, _overlayRenderer, _propBlock, forceActive: true);
        if (_rimRenderer != null)
            ApplyAlpha(lingerAlpha * rimAlphaMultiplier, c, _rimRenderer, _rimPropBlock, forceActive: true);
    }

    public void Hide()
    {
        if (_flashCoroutine  != null) StopCoroutine(_flashCoroutine);
        if (_lingerCoroutine != null) StopCoroutine(_lingerCoroutine);
        if (_overlayRenderer != null) ApplyAlpha(0f, Color.white, _overlayRenderer, _propBlock);
        if (_rimRenderer     != null) ApplyAlpha(0f, Color.white, _rimRenderer, _rimPropBlock);
    }

    // ─── Coroutines ───────────────────────────────────────────────────────────

    private IEnumerator FlashPulse(Color hitColor)
    {
        ApplyAlpha(peakAlpha, hitColor, _overlayRenderer, _propBlock, forceActive: true);
        if (_rimRenderer != null)
            ApplyAlpha(peakAlpha * rimAlphaMultiplier, hitColor, _rimRenderer, _rimPropBlock, forceActive: true);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float easedT = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / fadeDuration), 3f);
            float alpha  = Mathf.Lerp(peakAlpha, lingerAlpha, easedT);
            ApplyAlpha(alpha, hitColor, _overlayRenderer, _propBlock, forceActive: true);
            if (_rimRenderer != null)
                ApplyAlpha(alpha * rimAlphaMultiplier, hitColor, _rimRenderer, _rimPropBlock, forceActive: true);
            yield return null;
        }
        _flashCoroutine = null;
    }

    private IEnumerator LingerGlow(Color hitColor)
    {
        yield return new WaitForSeconds(fadeDuration + lingerDuration * 0.5f);
        float elapsed = 0f;
        float fadeTime = lingerDuration * 0.5f;
        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(lingerAlpha, 0f, elapsed / fadeTime);
            ApplyAlpha(alpha, hitColor, _overlayRenderer, _propBlock, forceActive: true);
            if (_rimRenderer != null)
                ApplyAlpha(alpha * rimAlphaMultiplier, hitColor, _rimRenderer, _rimPropBlock, forceActive: true);
            yield return null;
        }
        ApplyAlpha(0f, hitColor, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            ApplyAlpha(0f, hitColor, _rimRenderer, _rimPropBlock);
        _lingerCoroutine = null;
    }

    // ─── Build Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates per-instance materials using the best available shader pipeline.
    /// Priority: URP Unlit → Built-in Transparent → fallback pink (always visible).
    /// </summary>
    private bool BuildMaterials()
    {
        Shader overlayShader = FindBestTransparentShader();
        if (overlayShader == null)
        {
            Debug.LogError("[BHV] Could not find ANY usable transparent shader. Materials will be null.");
            return false;
        }
        Debug.Log($"[BHV] Using shader: '{overlayShader.name}'");

        _overlayMat = new Material(overlayShader) { name = "BossHitbox_Overlay" };
        ConfigureTransparentMaterial(_overlayMat, additive: false);

        _rimMat = new Material(overlayShader) { name = "BossHitbox_Rim" };
        ConfigureTransparentMaterial(_rimMat, additive: true);

        return true;
    }

    private static Shader FindBestTransparentShader()
    {
        // Try URP/HDRP unlit first (most projects in 2024+ use URP)
        string[] candidates = new[]
        {
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Unlit/Color",
            "Sprites/Default",
            "Standard",
            "Legacy Shaders/Transparent/Diffuse"
        };

        foreach (var name in candidates)
        {
            var s = Shader.Find(name);
            if (s != null)
            {
                Debug.Log($"[BHV] Found shader candidate: '{name}'");
                return s;
            }
        }
        return null;
    }

    private static void ConfigureTransparentMaterial(Material mat, bool additive)
    {
        // Universal property names that work across Built-in & URP shaders
        mat.SetFloat("_Mode",    3);   // Standard: Transparent
        mat.SetInt("_ZWrite",    0);
        mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",  additive
            ? (int)UnityEngine.Rendering.BlendMode.One                 // Additive
            : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);  // Alpha blend

        // Standard shader keywords
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

        // URP surface type keywords
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        mat.renderQueue = additive ? 3001 : 3000;

        // Set a visible default color (white semi-transparent) so it's obvious if rendering works
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", new Color(1f, 1f, 1f, 0.8f));
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.8f));
    }

    private bool BuildOverlayObjects()
    {
        // Auto-detect mesh
        if (overlayMesh == null)
        {
            var mf = GetComponent<MeshFilter>() ?? GetComponentInChildren<MeshFilter>();
            if (mf != null)
            {
                overlayMesh = mf.sharedMesh;
                Debug.Log($"[BHV] Auto-detected mesh '{overlayMesh?.name}' from MeshFilter on '{mf.gameObject.name}'");
            }
        }

        if (overlayMesh == null)
        {
            Debug.LogWarning($"[BHV] '{name}': No mesh found. Assign Overlay Mesh in the Inspector.");
            return false;
        }

        Debug.Log($"[BHV] Building overlay objects for '{name}' using mesh '{overlayMesh.name}'");
        (_overlayGO, _overlayRenderer) = CreateOverlayChild("_HitOverlay", overlayScaleBias, _overlayMat);

        if (enableRimOverlay)
            (_rimGO, _rimRenderer) = CreateOverlayChild("_HitRim", rimScaleBias, _rimMat);

        return _overlayRenderer != null;
    }

    private (GameObject, MeshRenderer) CreateOverlayChild(string childName, float scale, Material mat)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one * scale;
        go.layer = gameObject.layer;
        // NOTE: keep active=true so SetPropertyBlock takes effect immediately;
        // visibility is controlled by alpha via MaterialPropertyBlock.
        go.SetActive(true);

        var mf       = go.AddComponent<MeshFilter>();
        mf.sharedMesh = overlayMesh;

        var mr                   = go.AddComponent<MeshRenderer>();
        mr.material              = mat;   // instance assignment (not sharedMaterial)
        mr.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows        = false;
        mr.lightProbeUsage       = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage  = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        Debug.Log($"[BHV] Created child '{childName}' scale={scale} mat='{mat?.name}' shader='{mat?.shader?.name}'");
        return (go, mr);
    }

    // ─── Apply Alpha ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sets color+alpha via MaterialPropertyBlock and optionally forces the GO active.
    /// IMPORTANT: SetActive must be called AFTER SetPropertyBlock, otherwise
    /// the block is lost on re-activation in some Unity versions.
    /// </summary>
    private void ApplyAlpha(float alpha, Color baseColor, MeshRenderer rend,
                            MaterialPropertyBlock block, bool forceActive = false)
    {
        if (rend == null) return;

        Color c = baseColor;
        c.a = alpha;

        // Write to both _Color (Built-in/Standard) and _BaseColor (URP Lit/Unlit)
        block.SetColor("_Color",     c);
        block.SetColor("_BaseColor", c);
        rend.SetPropertyBlock(block);

        if (!forceActive)
            rend.gameObject.SetActive(alpha > 0.004f);
    }

    // ─── Color Encoding ───────────────────────────────────────────────────────

    private Color HPRatioToColor(float ratio)
    {
        if (ratio > midThreshold)
            return Color.Lerp(colorDamaged,   colorHealthy,  (ratio - midThreshold) / (1f - midThreshold));
        if (ratio > lowThreshold)
            return Color.Lerp(colorCritical,  colorDamaged,  (ratio - lowThreshold)  / (midThreshold - lowThreshold));
        if (ratio > critThreshold)
            return Color.Lerp(colorDestroyed, colorCritical, (ratio - critThreshold) / (lowThreshold  - critThreshold));
        return colorDestroyed;
    }

    // ─── Editor Gizmos ────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()          { if ( alwaysShowGizmo) DrawHitboxGizmo(); }
    private void OnDrawGizmosSelected()  { if (!alwaysShowGizmo) DrawHitboxGizmo(); }

    private void DrawHitboxGizmo()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Color solid = gizmoColor;
        Color wire  = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);

        switch (col)
        {
            case BoxCollider box:
                Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                Gizmos.color  = solid; Gizmos.DrawCube(box.center, box.size);
                Gizmos.color  = wire;  Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = Matrix4x4.identity;
                break;

            case SphereCollider sphere:
                float r = sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
                Vector3 wc = transform.TransformPoint(sphere.center);
                Gizmos.color = solid; Gizmos.DrawSphere(wc, r);
                Gizmos.color = wire;  Gizmos.DrawWireSphere(wc, r);
                break;

            case CapsuleCollider capsule:
                Vector3 cc = transform.TransformPoint(capsule.center);
                float cr = capsule.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                Gizmos.color = solid; Gizmos.DrawSphere(cc, cr);
                Gizmos.color = wire;  Gizmos.DrawWireSphere(cc, cr);
                break;

            default:
                Gizmos.color = wire;
                Gizmos.DrawWireSphere(transform.position, 0.3f);
                break;
        }
    }
#endif
}
