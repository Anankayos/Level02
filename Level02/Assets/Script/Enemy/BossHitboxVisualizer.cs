using UnityEngine;
using System.Collections;

/// <summary>
/// BossHitboxVisualizer — HZD-style hitbox feedback, URP-safe.
///
/// Mesh detection priority:
///   1. Inspector-assigned overlayMesh (static mesh only)
///   2. MeshFilter anywhere in subtree            → static overlay
///   3. SkinnedMeshRenderer anywhere in subtree   → live re-baked every LateUpdate
///   4. Collider primitive fallback               → Box / Sphere / Capsule proxy mesh
///
/// For skinned meshes the overlay MeshFilter is re-baked each LateUpdate so it
/// always matches the current animation pose (legs, arms, etc.).
///
/// Filter Console by "[BHV]" to trace all steps.
/// </summary>
[RequireComponent(typeof(BossPart))]
public class BossHitboxVisualizer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Overlay Mesh")]
    [Tooltip("Leave empty for auto-detection (MeshFilter / SkinnedMeshRenderer / Collider fallback).")]
    [SerializeField] private Mesh overlayMesh;
    [SerializeField] private float overlayScaleBias = 1.02f;

    [Header("Pulse Settings")]
    [SerializeField] [Range(0f,1f)]   private float peakAlpha    = 0.75f;
    [SerializeField]                  private float fadeDuration  = 0.35f;
    [SerializeField] [Range(0f,0.5f)] private float lingerAlpha  = 0.18f;
    [SerializeField]                  private float lingerDuration = 1.2f;

    [Header("Color Encoding")]
    [SerializeField] private Color colorHealthy  = new Color(0.2f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color colorDamaged  = new Color(1.0f, 0.8f, 0.1f, 1f);
    [SerializeField] private Color colorCritical = new Color(1.0f, 0.3f, 0.05f, 1f);
    [SerializeField] private Color colorDestroyed= new Color(1.0f, 0.05f, 0.05f, 1f);
    [SerializeField] [Range(0f,1f)] private float midThreshold  = 0.6f;
    [SerializeField] [Range(0f,1f)] private float lowThreshold  = 0.3f;
    [SerializeField] [Range(0f,1f)] private float critThreshold = 0.10f;

    [Header("Rim Overlay")]
    [SerializeField] private bool  enableRimOverlay   = true;
    [SerializeField] [Range(1.01f,1.15f)] private float rimScaleBias       = 1.06f;
    [SerializeField] [Range(0f,1f)]       private float rimAlphaMultiplier = 0.4f;

    [Header("Debug / Editor")]
    [SerializeField] private bool  alwaysShowGizmo = true;
    [SerializeField] private Color gizmoColor      = new Color(0f, 1f, 0.5f, 0.25f);
    [Tooltip("Set > 0 in Play mode to force the overlay visible immediately — no hit needed.")]
    [SerializeField] [Range(0f,1f)] private float debugForceAlpha = 0f;

    // ─── Private ──────────────────────────────────────────────────────────────
    private GameObject   _overlayGO;
    private GameObject   _rimGO;
    private MeshFilter   _overlayMF;   // needed for live re-bake
    private MeshFilter   _rimMF;
    private MeshRenderer _overlayRenderer;
    private MeshRenderer _rimRenderer;
    private MaterialPropertyBlock _propBlock;
    private MaterialPropertyBlock _rimPropBlock;
    private Coroutine _flashCoroutine;
    private Coroutine _lingerCoroutine;
    private bool _ready = false;
    private Material _overlayMat;
    private Material _rimMat;

    // Skinned mesh tracking
    private SkinnedMeshRenderer _trackedSMR = null;  // non-null → re-bake mode
    private Mesh _bakedMesh = null;                   // reused buffer (avoid GC)

    // ─── Unity ────────────────────────────────────────────────────────────────
    private void Awake()
    {
        Debug.Log($"[BHV] Awake on '{name}'");
        _propBlock    = new MaterialPropertyBlock();
        _rimPropBlock = new MaterialPropertyBlock();
        _ready = BuildMaterials() && BuildOverlayObjects();
        Debug.Log($"[BHV] Awake complete on '{name}'. _ready={_ready}  skinned={_trackedSMR != null}");
    }

    private void Start()
    {
        if (!_ready)
        {
            Debug.LogError($"[BHV] '{name}' NOT ready — overlay will not show. Check warnings above.");
            return;
        }
        ApplyAlpha(0f, Color.white, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            ApplyAlpha(0f, Color.white, _rimRenderer, _rimPropBlock);
        Debug.Log($"[BHV] '{name}' ready OK.");
    }

    private void LateUpdate()
    {
        // Re-bake skinned mesh every frame so overlay matches the live animation pose
        if (_ready && _trackedSMR != null && _bakedMesh != null)
        {
            _trackedSMR.BakeMesh(_bakedMesh);
            if (_overlayMF != null) _overlayMF.sharedMesh = _bakedMesh;
            if (_rimMF     != null) _rimMF.sharedMesh     = _bakedMesh;
        }

        // Debug knob: drag debugForceAlpha > 0 in Inspector to verify rendering
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
            Debug.LogWarning($"[BHV] NotifyHit on '{name}' but _ready=false.");
            return;
        }
        float ratio    = Mathf.Clamp01(currentHP / Mathf.Max(maxHP, 0.001f));
        Color hitColor = HPRatioToColor(ratio);
        Debug.Log($"[BHV] Hit '{name}' dmg={damage} hp={currentHP}/{maxHP} ratio={ratio:F2} color={hitColor}");
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
        if (_rimRenderer     != null) ApplyAlpha(0f, Color.white, _rimRenderer,     _rimPropBlock);
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
        float elapsed  = 0f;
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

    private bool BuildMaterials()
    {
        Shader s = FindBestTransparentShader();
        if (s == null) { Debug.LogError("[BHV] No usable transparent shader found."); return false; }
        Debug.Log($"[BHV] Using shader '{s.name}' on '{name}'");

        _overlayMat = new Material(s) { name = "BossHitbox_Overlay" };
        ConfigureTransparentMaterial(_overlayMat, additive: false);

        _rimMat = new Material(s) { name = "BossHitbox_Rim" };
        ConfigureTransparentMaterial(_rimMat, additive: true);
        return true;
    }

    private static Shader FindBestTransparentShader()
    {
        string[] candidates =
        {
            "Universal Render Pipeline/Lit",       // matches M_ISO_Mech pipeline
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Unlit/Color",
            "Sprites/Default",
            "Standard",
            "Legacy Shaders/Transparent/Diffuse"
        };
        foreach (var n in candidates)
        {
            var s = Shader.Find(n);
            if (s != null) { Debug.Log($"[BHV] Shader candidate found: '{n}'"); return s; }
        }
        return null;
    }

    private static void ConfigureTransparentMaterial(Material mat, bool additive)
    {
        // Surface type → Transparent for both URP Lit and Unlit
        mat.SetFloat("_Surface",  1f);   // URP: 0=Opaque 1=Transparent
        mat.SetFloat("_Mode",     3f);   // Built-in: Transparent
        mat.SetFloat("_Blend",    additive ? 2f : 0f); // URP: 0=Alpha 2=Additive
        mat.SetInt("_ZWrite",     0);
        mat.SetInt("_SrcBlend",   (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",   additive
            ? (int)UnityEngine.Rendering.BlendMode.One
            : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_Cull",       0);    // render both faces (avoids inside-out normals on baked mesh)
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = additive ? 3001 : 3000;
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     Color.white);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
    }

    private bool BuildOverlayObjects()
    {
        // Try static mesh first
        if (overlayMesh == null)
            overlayMesh = FindStaticMesh();

        // Try skinned mesh (sets _trackedSMR, no bake yet)
        if (overlayMesh == null)
            overlayMesh = FindSkinnedMesh();   // returns an empty Mesh buffer; _trackedSMR is set

        // Collider primitive last resort
        if (overlayMesh == null)
            overlayMesh = MeshFromCollider();

        if (overlayMesh == null)
        {
            Debug.LogWarning($"[BHV] '{name}': No mesh found via any method. Assign Overlay Mesh manually.");
            return false;
        }

        Debug.Log($"[BHV] '{name}': mesh='{overlayMesh.name}'  skinned={_trackedSMR != null}");
        (_overlayGO, _overlayRenderer, _overlayMF) = CreateOverlayChild("_HitOverlay", overlayScaleBias, _overlayMat);
        if (enableRimOverlay)
            (_rimGO, _rimRenderer, _rimMF) = CreateOverlayChild("_HitRim", rimScaleBias, _rimMat);

        return _overlayRenderer != null;
    }

    // Returns first static MeshFilter mesh found in subtree
    private Mesh FindStaticMesh()
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>(includeInactive: true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.gameObject.name.StartsWith("_Hit")) continue;
            Debug.Log($"[BHV] MeshFilter found on '{mf.gameObject.name}' mesh='{mf.sharedMesh.name}'");
            return mf.sharedMesh;
        }
        return null;
    }

    // Finds a SkinnedMeshRenderer, stores it in _trackedSMR, and returns an
    // EMPTY Mesh buffer that LateUpdate will fill every frame.
    private Mesh FindSkinnedMesh()
    {
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
        {
            if (smr.sharedMesh == null) continue;
            if (smr.gameObject.name.StartsWith("_Hit")) continue;
            _trackedSMR = smr;
            _bakedMesh  = new Mesh { name = smr.sharedMesh.name + "_LiveBake" };
            // Do an initial bake so the mesh isn't empty on frame 1
            smr.BakeMesh(_bakedMesh);
            Debug.Log($"[BHV] SkinnedMeshRenderer on '{smr.gameObject.name}' — live re-bake enabled.");
            return _bakedMesh;
        }
        return null;
    }

    private Mesh MeshFromCollider()
    {
        var col = GetComponent<Collider>();
        if (col == null) { Debug.LogWarning($"[BHV] '{name}': No Collider — nothing to visualize."); return null; }
        Mesh m = null;
        switch (col)
        {
            case BoxCollider box:
                m = CreateBoxMesh(box.size); m.name = "Hitbox_Box";
                Debug.Log($"[BHV] '{name}': Box primitive mesh (size={box.size})");
                break;
            case SphereCollider sphere:
                m = CreateSphereMesh(sphere.radius, 16, 12); m.name = "Hitbox_Sphere";
                Debug.Log($"[BHV] '{name}': Sphere primitive mesh (r={sphere.radius})");
                break;
            case CapsuleCollider capsule:
                m = CreateCapsuleMesh(capsule.radius, capsule.height, 12); m.name = "Hitbox_Capsule";
                Debug.Log($"[BHV] '{name}': Capsule primitive mesh (r={capsule.radius} h={capsule.height})");
                break;
            default:
                Debug.LogWarning($"[BHV] '{name}': Unsupported collider type {col.GetType().Name}.");
                break;
        }
        return m;
    }

    private (GameObject, MeshRenderer, MeshFilter) CreateOverlayChild(string childName, float scale, Material mat)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one * scale;
        go.layer = gameObject.layer;
        go.SetActive(true);

        var mf        = go.AddComponent<MeshFilter>();
        mf.sharedMesh = overlayMesh;

        var mr                  = go.AddComponent<MeshRenderer>();
        mr.material             = mat;
        mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        Debug.Log($"[BHV] Created '{childName}' under '{name}' scale={scale} shader='{mat?.shader?.name}'");
        return (go, mr, mf);
    }

    // ─── Apply Alpha ──────────────────────────────────────────────────────────

    private void ApplyAlpha(float alpha, Color baseColor, MeshRenderer rend,
                            MaterialPropertyBlock block, bool forceActive = false)
    {
        if (rend == null) return;
        Color c = baseColor;
        c.a = alpha;
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

    // ─── Primitive Mesh Builders ──────────────────────────────────────────────

    private static Mesh CreateBoxMesh(Vector3 size)
    {
        var mesh = new Mesh();
        Vector3 h = size * 0.5f;
        mesh.vertices = new Vector3[]
        {
            new(-h.x,-h.y, h.z), new( h.x,-h.y, h.z), new( h.x, h.y, h.z), new(-h.x, h.y, h.z),
            new( h.x,-h.y,-h.z), new(-h.x,-h.y,-h.z), new(-h.x, h.y,-h.z), new( h.x, h.y,-h.z),
            new(-h.x,-h.y,-h.z), new(-h.x,-h.y, h.z), new(-h.x, h.y, h.z), new(-h.x, h.y,-h.z),
            new( h.x,-h.y, h.z), new( h.x,-h.y,-h.z), new( h.x, h.y,-h.z), new( h.x, h.y, h.z),
            new(-h.x, h.y, h.z), new( h.x, h.y, h.z), new( h.x, h.y,-h.z), new(-h.x, h.y,-h.z),
            new(-h.x,-h.y,-h.z), new( h.x,-h.y,-h.z), new( h.x,-h.y, h.z), new(-h.x,-h.y, h.z),
        };
        mesh.triangles = new int[]
        {
            0,1,2, 0,2,3,   4,5,6, 4,6,7,
            8,9,10, 8,10,11, 12,13,14, 12,14,15,
            16,17,18, 16,18,19, 20,21,22, 20,22,23
        };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreateSphereMesh(float radius, int longSegs, int latSegs)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();
        for (int lat = 0; lat <= latSegs; lat++)
        {
            float theta = Mathf.PI * lat / latSegs;
            for (int lon = 0; lon <= longSegs; lon++)
            {
                float phi = 2f * Mathf.PI * lon / longSegs;
                verts.Add(new Vector3(
                    radius * Mathf.Sin(theta) * Mathf.Cos(phi),
                    radius * Mathf.Cos(theta),
                    radius * Mathf.Sin(theta) * Mathf.Sin(phi)));
            }
        }
        int w = longSegs + 1;
        for (int lat = 0; lat < latSegs; lat++)
            for (int lon = 0; lon < longSegs; lon++)
            {
                int a = lat*w+lon, b = a+w, c = a+1, d = b+1;
                tris.AddRange(new[]{a,b,c, b,d,c});
            }
        var mesh = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Mesh CreateCapsuleMesh(float radius, float height, int segs)
    {
        float bodyHalf = Mathf.Max(0f, height * 0.5f - radius);
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();
        int hemi = segs / 2;
        for (int section = 0; section < 2; section++)
        {
            float ySign = section == 0 ? 1f : -1f;
            for (int lat = 0; lat <= hemi; lat++)
            {
                float theta = Mathf.PI * 0.5f * lat / hemi;
                for (int lon = 0; lon <= segs; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / segs;
                    verts.Add(new Vector3(
                        radius * Mathf.Cos(theta) * Mathf.Cos(phi),
                        ySign * (bodyHalf + radius * Mathf.Sin(theta)),
                        radius * Mathf.Cos(theta) * Mathf.Sin(phi)));
                }
            }
        }
        int w = segs + 1, total = (hemi + 1) * w;
        for (int section = 0; section < 2; section++)
        {
            int off = section * total;
            for (int lat = 0; lat < hemi; lat++)
                for (int lon = 0; lon < segs; lon++)
                {
                    int a = off+lat*w+lon, b = a+w, c = a+1, d = b+1;
                    tris.AddRange(section == 0 ? new[]{a,b,c,b,d,c} : new[]{a,c,b,b,c,d});
                }
        }
        int topRing = hemi * w, botRing = total;
        for (int lon = 0; lon < segs; lon++)
        {
            int a = topRing+lon, b = topRing+lon+1, c = botRing+lon, d = botRing+lon+1;
            tris.AddRange(new[]{a,c,b, b,c,d});
        }
        var mesh = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        mesh.RecalculateNormals();
        return mesh;
    }

    // ─── Editor Gizmos ────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()         { if ( alwaysShowGizmo) DrawHitboxGizmo(); }
    private void OnDrawGizmosSelected() { if (!alwaysShowGizmo) DrawHitboxGizmo(); }

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
                float r  = sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
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
