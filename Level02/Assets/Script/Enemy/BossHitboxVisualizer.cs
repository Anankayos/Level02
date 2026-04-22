using UnityEngine;
using System.Collections;

/// <summary>
/// BossHitboxVisualizer — HZD-style hitbox feedback, URP-safe.
///
/// Mesh detection (in order):
///   1. Inspector-assigned overlayMesh              → static overlay on this GO
///   2. MeshFilter anywhere in own subtree           → static overlay on this GO
///   3. SkinnedMeshRenderer anywhere in ROOT tree    → live-baked overlay parented to SMR
///   4. Collider primitive (Box/Sphere/Capsule)      → static overlay on this GO
///
/// For skinned meshes the script walks UP to the hierarchy root, then searches
/// the entire tree downward — so it finds SK_Mech_LOD0 even when BossPart sits
/// on a sibling bone (FrontLegL, MiddleLegR, etc.).
/// The overlay child is parented directly to the SMR's GameObject so the baked
/// mesh is already in the correct local space.
///
/// Filter Console by "[BHV]" to trace all steps.
/// </summary>
[RequireComponent(typeof(BossPart))]
public class BossHitboxVisualizer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Overlay Mesh")]
    [Tooltip("Leave empty for auto-detection.")]
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
    [SerializeField] private bool  alwaysShowGizmo  = true;
    [SerializeField] private Color gizmoColor       = new Color(0f, 1f, 0.5f, 0.25f);
    [Tooltip("Set > 0 in Play mode to force overlay visible without needing a hit.")]
    [SerializeField] [Range(0f,1f)] private float debugForceAlpha = 0f;

    // ─── Private state ────────────────────────────────────────────────────────
    private MeshFilter   _overlayMF;
    private MeshFilter   _rimMF;
    private MeshRenderer _overlayRenderer;
    private MeshRenderer _rimRenderer;
    private MaterialPropertyBlock _propBlock;
    private MaterialPropertyBlock _rimPropBlock;
    private Coroutine _flashCoroutine;
    private Coroutine _lingerCoroutine;
    private bool     _ready       = false;
    private Material _overlayMat;
    private Material _rimMat;

    // Skinned mesh live-bake
    private SkinnedMeshRenderer _trackedSMR  = null;
    private Mesh                _bakedMesh   = null;
    // When using SMR the overlays are parented to the SMR GO, not this GO
    private Transform           _overlayRoot = null;

    // ─── Unity ────────────────────────────────────────────────────────────────
    private void Awake()
    {
        Debug.Log($"[BHV] Awake on '{name}'");
        _propBlock    = new MaterialPropertyBlock();
        _rimPropBlock = new MaterialPropertyBlock();
        _ready = BuildMaterials() && BuildOverlayObjects();
        Debug.Log($"[BHV] '{name}' _ready={_ready}  skinned={_trackedSMR != null}" +
                  $"  overlayRoot='{_overlayRoot?.name}'");
    }

    private void Start()
    {
        if (!_ready)
        {
            Debug.LogError($"[BHV] '{name}' NOT ready — check warnings above.");
            return;
        }
        ApplyAlpha(0f, Color.white, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            ApplyAlpha(0f, Color.white, _rimRenderer, _rimPropBlock);
        Debug.Log($"[BHV] '{name}' started OK.");
    }

    private void LateUpdate()
    {
        if (!_ready) return;

        // Re-bake skinned mesh every frame to match live animation
        if (_trackedSMR != null && _bakedMesh != null)
        {
            _trackedSMR.BakeMesh(_bakedMesh);
            if (_overlayMF != null) _overlayMF.sharedMesh = _bakedMesh;
            if (_rimMF     != null) _rimMF.sharedMesh     = _bakedMesh;
        }

        // Debug knob — drag in Inspector while in Play mode
        if (debugForceAlpha > 0f)
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
        if (!_ready) { Debug.LogWarning($"[BHV] NotifyHit on '{name}' but _ready=false."); return; }
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
            float t     = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / fadeDuration), 3f);
            float alpha = Mathf.Lerp(peakAlpha, lingerAlpha, t);
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
        if (_rimRenderer != null) ApplyAlpha(0f, hitColor, _rimRenderer, _rimPropBlock);
        _lingerCoroutine = null;
    }

    // ─── Build ─────────────────────────────────────────────────────────────────

    private bool BuildMaterials()
    {
        Shader s = FindBestShader();
        if (s == null) { Debug.LogError("[BHV] No usable transparent shader found."); return false; }
        Debug.Log($"[BHV] Shader: '{s.name}'");
        _overlayMat = new Material(s) { name = "BossHitbox_Overlay" };
        ConfigureMat(_overlayMat, additive: false);
        _rimMat = new Material(s) { name = "BossHitbox_Rim" };
        ConfigureMat(_rimMat, additive: true);
        return true;
    }

    private static Shader FindBestShader()
    {
        string[] list =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Unlit/Color",
            "Sprites/Default",
            "Standard",
            "Legacy Shaders/Transparent/Diffuse"
        };
        foreach (var n in list) { var s = Shader.Find(n); if (s != null) return s; }
        return null;
    }

    private static void ConfigureMat(Material mat, bool additive)
    {
        mat.SetFloat("_Surface", 1f);   // URP: Transparent
        mat.SetFloat("_Mode",    3f);   // Built-in: Transparent
        mat.SetFloat("_Blend",   additive ? 2f : 0f);
        mat.SetInt("_ZWrite",    0);
        mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",  additive
            ? (int)UnityEngine.Rendering.BlendMode.One
            : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_Cull", 0); // double-sided
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
        // 1. Inspector mesh — static, parented to this GO
        if (overlayMesh != null)
        {
            _overlayRoot = transform;
            Debug.Log($"[BHV] '{name}': using Inspector-assigned mesh.");
            return SpawnOverlays();
        }

        // 2. MeshFilter anywhere in own subtree — static, parented to this GO
        var mf = FindStaticMesh();
        if (mf != null)
        {
            overlayMesh  = mf.sharedMesh;
            _overlayRoot = transform;
            Debug.Log($"[BHV] '{name}': static mesh '{overlayMesh.name}' from MeshFilter on '{mf.gameObject.name}'.");
            return SpawnOverlays();
        }

        // 3. SkinnedMeshRenderer — search the ENTIRE hierarchy from the root
        var smr = FindSMRFromRoot();
        if (smr != null)
        {
            _trackedSMR  = smr;
            _bakedMesh   = new Mesh { name = smr.sharedMesh.name + "_LiveBake" };
            smr.BakeMesh(_bakedMesh);          // initial bake so frame 1 isn't empty
            overlayMesh  = _bakedMesh;
            _overlayRoot = smr.transform;      // parent overlays to the SMR's GO
            Debug.Log($"[BHV] '{name}': SMR '{smr.gameObject.name}' found — live-bake mode, overlayRoot='{_overlayRoot.name}'.");
            return SpawnOverlays();
        }

        // 4. Collider primitive fallback — parented to this GO
        overlayMesh = MeshFromCollider();
        if (overlayMesh != null)
        {
            _overlayRoot = transform;
            return SpawnOverlays();
        }

        Debug.LogWarning($"[BHV] '{name}': No mesh found via any method. Assign Overlay Mesh manually.");
        return false;
    }

    private MeshFilter FindStaticMesh()
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>(includeInactive: true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.gameObject.name.StartsWith("_Hit")) continue;
            return mf;
        }
        return null;
    }

    /// <summary>
    /// Walks UP to the scene root, then searches the entire tree downward.
    /// This finds SK_Mech_LOD0 even when BossPart is on a sibling bone.
    /// </summary>
    private SkinnedMeshRenderer FindSMRFromRoot()
    {
        // Find scene root (topmost transform with no parent)
        Transform root = transform;
        while (root.parent != null) root = root.parent;

        Debug.Log($"[BHV] '{name}': searching for SMR from root '{root.name}'");

        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true))
        {
            if (smr.sharedMesh == null) continue;
            if (smr.gameObject.name.StartsWith("_Hit")) continue;
            Debug.Log($"[BHV] '{name}': found SMR on '{smr.gameObject.name}' mesh='{smr.sharedMesh.name}'");
            return smr;
        }
        return null;
    }

    private bool SpawnOverlays()
    {
        (_overlayRenderer, _overlayMF) = CreateOverlayChild("_HitOverlay_" + name, overlayScaleBias, _overlayMat);
        if (enableRimOverlay)
            (_rimRenderer, _rimMF) = CreateOverlayChild("_HitRim_" + name, rimScaleBias, _rimMat);
        return _overlayRenderer != null;
    }

    private (MeshRenderer, MeshFilter) CreateOverlayChild(string childName, float scale, Material mat)
    {
        var go = new GameObject(childName);
        // Parent to _overlayRoot (SMR's GO for skinned, this GO for static)
        go.transform.SetParent(_overlayRoot, worldPositionStays: false);
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

        Debug.Log($"[BHV] Spawned '{childName}' under '{_overlayRoot.name}' scale={scale}");
        return (mr, mf);
    }

    // ─── Apply Alpha ──────────────────────────────────────────────────────────

    private void ApplyAlpha(float alpha, Color baseColor, MeshRenderer rend,
                            MaterialPropertyBlock block, bool forceActive = false)
    {
        if (rend == null) return;
        Color c = baseColor; c.a = alpha;
        block.SetColor("_Color",     c);
        block.SetColor("_BaseColor", c);
        rend.SetPropertyBlock(block);
        if (!forceActive) rend.gameObject.SetActive(alpha > 0.004f);
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

    // ─── Collider Primitive Fallback ───────────────────────────────────────────

    private Mesh MeshFromCollider()
    {
        var col = GetComponent<Collider>();
        if (col == null) { Debug.LogWarning($"[BHV] '{name}': No Collider found either."); return null; }
        Mesh m = null;
        switch (col)
        {
            case BoxCollider box:
                m = CreateBoxMesh(box.size); m.name = "Hitbox_Box";
                Debug.Log($"[BHV] '{name}': Box primitive (size={box.size})"); break;
            case SphereCollider sphere:
                m = CreateSphereMesh(sphere.radius, 16, 12); m.name = "Hitbox_Sphere";
                Debug.Log($"[BHV] '{name}': Sphere primitive (r={sphere.radius})"); break;
            case CapsuleCollider cap:
                m = CreateCapsuleMesh(cap.radius, cap.height, 12); m.name = "Hitbox_Capsule";
                Debug.Log($"[BHV] '{name}': Capsule primitive (r={cap.radius} h={cap.height})"); break;
            default:
                Debug.LogWarning($"[BHV] '{name}': Unsupported collider {col.GetType().Name}."); break;
        }
        return m;
    }

    // ─── Primitive Mesh Builders ──────────────────────────────────────────────

    private static Mesh CreateBoxMesh(Vector3 size)
    {
        var m = new Mesh();
        Vector3 h = size * 0.5f;
        m.vertices = new[]{
            new Vector3(-h.x,-h.y, h.z),new Vector3( h.x,-h.y, h.z),new Vector3( h.x, h.y, h.z),new Vector3(-h.x, h.y, h.z),
            new Vector3( h.x,-h.y,-h.z),new Vector3(-h.x,-h.y,-h.z),new Vector3(-h.x, h.y,-h.z),new Vector3( h.x, h.y,-h.z),
            new Vector3(-h.x,-h.y,-h.z),new Vector3(-h.x,-h.y, h.z),new Vector3(-h.x, h.y, h.z),new Vector3(-h.x, h.y,-h.z),
            new Vector3( h.x,-h.y, h.z),new Vector3( h.x,-h.y,-h.z),new Vector3( h.x, h.y,-h.z),new Vector3( h.x, h.y, h.z),
            new Vector3(-h.x, h.y, h.z),new Vector3( h.x, h.y, h.z),new Vector3( h.x, h.y,-h.z),new Vector3(-h.x, h.y,-h.z),
            new Vector3(-h.x,-h.y,-h.z),new Vector3( h.x,-h.y,-h.z),new Vector3( h.x,-h.y, h.z),new Vector3(-h.x,-h.y, h.z),
        };
        m.triangles = new[]{ 0,1,2,0,2,3, 4,5,6,4,6,7, 8,9,10,8,10,11, 12,13,14,12,14,15, 16,17,18,16,18,19, 20,21,22,20,22,23 };
        m.RecalculateNormals(); return m;
    }

    private static Mesh CreateSphereMesh(float r, int lng, int lat)
    {
        var v = new System.Collections.Generic.List<Vector3>();
        var t = new System.Collections.Generic.List<int>();
        for (int i=0;i<=lat;i++){ float th=Mathf.PI*i/lat;
            for (int j=0;j<=lng;j++){ float ph=2f*Mathf.PI*j/lng;
                v.Add(new Vector3(r*Mathf.Sin(th)*Mathf.Cos(ph),r*Mathf.Cos(th),r*Mathf.Sin(th)*Mathf.Sin(ph))); }}
        int w=lng+1;
        for (int i=0;i<lat;i++) for (int j=0;j<lng;j++){
            int a=i*w+j,b=a+w,c=a+1,d=b+1; t.AddRange(new[]{a,b,c,b,d,c}); }
        var m=new Mesh{vertices=v.ToArray(),triangles=t.ToArray()}; m.RecalculateNormals(); return m;
    }

    private static Mesh CreateCapsuleMesh(float r, float h, int segs)
    {
        float bh=Mathf.Max(0f,h*0.5f-r); int hemi=segs/2;
        var v=new System.Collections.Generic.List<Vector3>();
        var t=new System.Collections.Generic.List<int>();
        for (int s=0;s<2;s++){ float ys=s==0?1f:-1f;
            for (int i=0;i<=hemi;i++){ float th=Mathf.PI*0.5f*i/hemi;
                for (int j=0;j<=segs;j++){ float ph=2f*Mathf.PI*j/segs;
                    v.Add(new Vector3(r*Mathf.Cos(th)*Mathf.Cos(ph),ys*(bh+r*Mathf.Sin(th)),r*Mathf.Cos(th)*Mathf.Sin(ph))); }}}
        int w=segs+1,tot=(hemi+1)*w;
        for (int s=0;s<2;s++){ int off=s*tot;
            for (int i=0;i<hemi;i++) for (int j=0;j<segs;j++){
                int a=off+i*w+j,b=a+w,c=a+1,d=b+1;
                t.AddRange(s==0?new[]{a,b,c,b,d,c}:new[]{a,c,b,b,c,d}); }}
        int tr=hemi*w,br=tot;
        for (int j=0;j<segs;j++){ int a=tr+j,b=tr+j+1,c=br+j,d=br+j+1; t.AddRange(new[]{a,c,b,b,c,d}); }
        var m=new Mesh{vertices=v.ToArray(),triangles=t.ToArray()}; m.RecalculateNormals(); return m;
    }

    // ─── Editor Gizmos ────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmos()         { if ( alwaysShowGizmo) DrawGizmo(); }
    private void OnDrawGizmosSelected() { if (!alwaysShowGizmo) DrawGizmo(); }
    private void DrawGizmo()
    {
        var col = GetComponent<Collider>(); if (col == null) return;
        Color solid = gizmoColor;
        Color wire  = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        switch (col)
        {
            case BoxCollider box:
                Gizmos.matrix = Matrix4x4.TRS(transform.position,transform.rotation,transform.lossyScale);
                Gizmos.color=solid; Gizmos.DrawCube(box.center,box.size);
                Gizmos.color=wire;  Gizmos.DrawWireCube(box.center,box.size);
                Gizmos.matrix=Matrix4x4.identity; break;
            case SphereCollider sp:
                float rr=sp.radius*Mathf.Max(transform.lossyScale.x,transform.lossyScale.y,transform.lossyScale.z);
                Vector3 wc=transform.TransformPoint(sp.center);
                Gizmos.color=solid; Gizmos.DrawSphere(wc,rr);
                Gizmos.color=wire;  Gizmos.DrawWireSphere(wc,rr); break;
            case CapsuleCollider cap:
                Vector3 cc=transform.TransformPoint(cap.center);
                float cr=cap.radius*Mathf.Max(transform.lossyScale.x,transform.lossyScale.z);
                Gizmos.color=solid; Gizmos.DrawSphere(cc,cr);
                Gizmos.color=wire;  Gizmos.DrawWireSphere(cc,cr); break;
            default:
                Gizmos.color=wire; Gizmos.DrawWireSphere(transform.position,0.3f); break;
        }
    }
#endif
}
