using UnityEngine;
using System.Collections;

/// <summary>
/// BossHitboxVisualizer — HZD-style per-part hitbox feedback, URP-safe.
///
/// Priority order (first match wins):
///   1. Inspector-assigned overlayMesh
///   2. MeshFilter anywhere in own subtree (static mesh part)
///   3. Collider on this GameObject (Box / Sphere / Capsule) — PRIMARY path
///      for skinned bosses where BossPart bones have physics colliders.
///
/// The overlay is a primitive mesh that exactly matches the collider shape,
/// parented to this transform so it follows bone animation automatically.
///
/// Filter Console by "[BHV]" to trace all steps.
/// </summary>
[RequireComponent(typeof(BossPart))]
public class BossHitboxVisualizer : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────
    [Header("Overlay Mesh")]
    [Tooltip("Leave empty — auto-detected from collider.")]
    [SerializeField] private Mesh overlayMesh;
    [SerializeField] private float overlayScaleBias = 1.04f;

    [Header("Pulse Settings")]
    [SerializeField] [Range(0f,1f)]   private float peakAlpha     = 0.75f;
    [SerializeField]                  private float fadeDuration   = 0.35f;
    [SerializeField] [Range(0f,0.5f)] private float lingerAlpha   = 0.20f;
    [SerializeField]                  private float lingerDuration = 1.2f;

    [Header("Color Encoding")]
    [SerializeField] private Color colorHealthy  = new Color(0.2f, 0.9f, 0.4f, 1f);
    [SerializeField] private Color colorDamaged  = new Color(1.0f, 0.8f, 0.1f, 1f);
    [SerializeField] private Color colorCritical = new Color(1.0f, 0.3f, 0.05f, 1f);
    [SerializeField] private Color colorDestroyed= new Color(1.0f, 0.05f, 0.05f, 1f);
    [SerializeField] [Range(0f,1f)] private float midThreshold   = 0.60f;
    [SerializeField] [Range(0f,1f)] private float lowThreshold   = 0.30f;
    [SerializeField] [Range(0f,1f)] private float critThreshold  = 0.10f;

    [Header("Rim Overlay")]
    [SerializeField] private bool  enableRimOverlay    = true;
    [SerializeField] [Range(1.01f,1.20f)] private float rimScaleBias        = 1.10f;
    [SerializeField] [Range(0f,1f)]       private float rimAlphaMultiplier  = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool  alwaysShowGizmo  = true;
    [SerializeField] private Color gizmoColor       = new Color(0f, 1f, 0.5f, 0.25f);
    [Tooltip("Drag > 0 in Play mode to force overlay visible without a hit.")]
    [SerializeField] [Range(0f,1f)] private float debugForceAlpha = 0f;

    // ─── Private state ────────────────────────────────────────────────────
    private MeshFilter            _overlayMF;
    private MeshFilter            _rimMF;
    private MeshRenderer          _overlayRenderer;
    private MeshRenderer          _rimRenderer;
    private MaterialPropertyBlock _propBlock;
    private MaterialPropertyBlock _rimPropBlock;
    private Coroutine             _flashCoroutine;
    private Coroutine             _lingerCoroutine;
    private bool                  _ready = false;
    private Material              _overlayMat;
    private Material              _rimMat;
    // Collider centre offset (for box/sphere/capsule that have a local centre)
    private Vector3               _colliderCenter = Vector3.zero;

    // ─── Unity ────────────────────────────────────────────────────────────
    private void Awake()
    {
        Debug.Log($"[BHV] Awake on '{name}'");
        _propBlock    = new MaterialPropertyBlock();
        _rimPropBlock = new MaterialPropertyBlock();
        _ready        = BuildMaterials() && BuildOverlayObjects();
        Debug.Log($"[BHV] '{name}' _ready={_ready}");
    }

    private void Start()
    {
        if (!_ready) { Debug.LogError($"[BHV] '{name}' NOT ready — check warnings above."); return; }
        ApplyAlpha(0f, Color.white, _overlayRenderer, _propBlock);
        if (_rimRenderer != null)
            ApplyAlpha(0f, Color.white, _rimRenderer, _rimPropBlock);
        Debug.Log($"[BHV] '{name}' started OK.");
    }

    private void LateUpdate()
    {
        if (!_ready) return;
        if (debugForceAlpha > 0f)
        {
            Color c = HPRatioToColor(1f - debugForceAlpha);
            ApplyAlpha(debugForceAlpha, c, _overlayRenderer, _propBlock, forceActive: true);
            if (_rimRenderer != null)
                ApplyAlpha(debugForceAlpha * rimAlphaMultiplier, c, _rimRenderer, _rimPropBlock, forceActive: true);
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────

    public void NotifyHit(float damage, float currentHP, float maxHP)
    {
        if (!_ready) { Debug.LogWarning($"[BHV] NotifyHit on '{name}' but _ready=false."); return; }
        float ratio    = Mathf.Clamp01(currentHP / Mathf.Max(maxHP, 0.001f));
        Color hitColor = HPRatioToColor(ratio);
        Debug.Log($"[BHV] Hit '{name}' dmg={damage} hp={currentHP}/{maxHP} ratio={ratio:F2}");
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

    // ─── Coroutines ───────────────────────────────────────────────────────

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

    // ─── Build ────────────────────────────────────────────────────────────

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
        string[] candidates =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Unlit/Color",
            "Sprites/Default",
            "Standard",
            "Legacy Shaders/Transparent/Diffuse"
        };
        foreach (var n in candidates) { var s = Shader.Find(n); if (s != null) return s; }
        return null;
    }

    private static void ConfigureMat(Material mat, bool additive)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Mode",    3f);
        mat.SetFloat("_Blend",   additive ? 2f : 0f);
        mat.SetInt("_ZWrite",    0);
        mat.SetInt("_SrcBlend",  (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend",  additive
            ? (int)UnityEngine.Rendering.BlendMode.One
            : (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_Cull", 0); // double-sided — visible from all angles
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
        // Priority 1: Inspector-assigned mesh
        if (overlayMesh != null)
        {
            Debug.Log($"[BHV] '{name}': using Inspector mesh.");
            return SpawnOverlays(Vector3.zero);
        }

        // Priority 2: Static MeshFilter in own subtree
        var mf = FindStaticMesh();
        if (mf != null)
        {
            overlayMesh = mf.sharedMesh;
            Debug.Log($"[BHV] '{name}': static mesh '{overlayMesh.name}' on '{mf.gameObject.name}'.");
            return SpawnOverlays(Vector3.zero);
        }

        // Priority 3: Collider primitive on this GameObject — main path for bone-parts
        var result = BuildFromCollider();
        if (result.mesh != null)
        {
            overlayMesh = result.mesh;
            Debug.Log($"[BHV] '{name}': collider primitive '{overlayMesh.name}' center={result.center}.");
            return SpawnOverlays(result.center);
        }

        Debug.LogWarning($"[BHV] '{name}': No mesh source found. " +
                         "Add a Box/Sphere/Capsule Collider or assign Overlay Mesh manually.");
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

    private (Mesh mesh, Vector3 center) BuildFromCollider()
    {
        var col = GetComponent<Collider>();
        if (col == null)
        {
            Debug.LogWarning($"[BHV] '{name}': No Collider on this GameObject.");
            return (null, Vector3.zero);
        }

        switch (col)
        {
            case BoxCollider box:
            {
                var m = CreateBoxMesh(box.size);
                m.name = "Hitbox_Box";
                Debug.Log($"[BHV] '{name}': Box (size={box.size} center={box.center})");
                return (m, box.center);
            }
            case SphereCollider sp:
            {
                var m = CreateSphereMesh(sp.radius, 16, 12);
                m.name = "Hitbox_Sphere";
                Debug.Log($"[BHV] '{name}': Sphere (r={sp.radius} center={sp.center})");
                return (m, sp.center);
            }
            case CapsuleCollider cap:
            {
                var m = CreateCapsuleMesh(cap.radius, cap.height, cap.direction, 16);
                m.name = "Hitbox_Capsule";
                Debug.Log($"[BHV] '{name}': Capsule (r={cap.radius} h={cap.height} dir={cap.direction} center={cap.center})");
                return (m, cap.center);
            }
            default:
                Debug.LogWarning($"[BHV] '{name}': Collider type {col.GetType().Name} not supported — assign Overlay Mesh manually.");
                return (null, Vector3.zero);
        }
    }

    private bool SpawnOverlays(Vector3 localCenter)
    {
        (_overlayRenderer, _overlayMF) = CreateOverlayChild("_HitOverlay_" + name, overlayScaleBias,  localCenter, _overlayMat);
        if (enableRimOverlay)
            (_rimRenderer, _rimMF)     = CreateOverlayChild("_HitRim_"     + name, rimScaleBias,      localCenter, _rimMat);
        return _overlayRenderer != null;
    }

    private (MeshRenderer, MeshFilter) CreateOverlayChild(
        string childName, float scale, Vector3 localCenter, Material mat)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, worldPositionStays: false);
        // Apply collider centre offset so the mesh sits exactly over the collider
        go.transform.localPosition = localCenter;
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

        Debug.Log($"[BHV] Spawned '{childName}' under '{name}' scale={scale} offset={localCenter}");
        return (mr, mf);
    }

    // ─── Apply Alpha ──────────────────────────────────────────────────────

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

    // ─── Color Encoding ───────────────────────────────────────────────────

    private Color HPRatioToColor(float ratio)
    {
        if (ratio > midThreshold)
            return Color.Lerp(colorDamaged,   colorHealthy,  (ratio - midThreshold)  / (1f - midThreshold));
        if (ratio > lowThreshold)
            return Color.Lerp(colorCritical,  colorDamaged,  (ratio - lowThreshold)  / (midThreshold - lowThreshold));
        if (ratio > critThreshold)
            return Color.Lerp(colorDestroyed, colorCritical, (ratio - critThreshold) / (lowThreshold  - critThreshold));
        return colorDestroyed;
    }

    // ─── Primitive Mesh Builders ──────────────────────────────────────────

    private static Mesh CreateBoxMesh(Vector3 size)
    {
        var m = new Mesh();
        Vector3 h = size * 0.5f;
        m.vertices = new[]
        {
            // Front
            new Vector3(-h.x,-h.y, h.z), new Vector3( h.x,-h.y, h.z),
            new Vector3( h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z),
            // Back
            new Vector3( h.x,-h.y,-h.z), new Vector3(-h.x,-h.y,-h.z),
            new Vector3(-h.x, h.y,-h.z), new Vector3( h.x, h.y,-h.z),
            // Left
            new Vector3(-h.x,-h.y,-h.z), new Vector3(-h.x,-h.y, h.z),
            new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y,-h.z),
            // Right
            new Vector3( h.x,-h.y, h.z), new Vector3( h.x,-h.y,-h.z),
            new Vector3( h.x, h.y,-h.z), new Vector3( h.x, h.y, h.z),
            // Top
            new Vector3(-h.x, h.y, h.z), new Vector3( h.x, h.y, h.z),
            new Vector3( h.x, h.y,-h.z), new Vector3(-h.x, h.y,-h.z),
            // Bottom
            new Vector3(-h.x,-h.y,-h.z), new Vector3( h.x,-h.y,-h.z),
            new Vector3( h.x,-h.y, h.z), new Vector3(-h.x,-h.y, h.z),
        };
        m.triangles = new[]
        {
            0,1,2, 0,2,3,   4,5,6, 4,6,7,
            8,9,10, 8,10,11, 12,13,14, 12,14,15,
            16,17,18, 16,18,19, 20,21,22, 20,22,23
        };
        m.RecalculateNormals();
        return m;
    }

    private static Mesh CreateSphereMesh(float r, int lng, int lat)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= lat; i++)
        {
            float theta = Mathf.PI * i / lat;
            for (int j = 0; j <= lng; j++)
            {
                float phi = 2f * Mathf.PI * j / lng;
                verts.Add(new Vector3(
                    r * Mathf.Sin(theta) * Mathf.Cos(phi),
                    r * Mathf.Cos(theta),
                    r * Mathf.Sin(theta) * Mathf.Sin(phi)));
            }
        }
        int w = lng + 1;
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lng; j++)
            {
                int a = i*w+j, b = a+w, c = a+1, d = b+1;
                tris.AddRange(new[]{ a, b, c, b, d, c });
            }
        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    /// <summary>direction: 0=X, 1=Y, 2=Z (matches CapsuleCollider.direction)</summary>
    private static Mesh CreateCapsuleMesh(float r, float height, int direction, int segs)
    {
        float bodyHalf = Mathf.Max(0f, height * 0.5f - r);
        int   hemi     = segs / 2;
        var   verts    = new System.Collections.Generic.List<Vector3>();
        var   tris     = new System.Collections.Generic.List<int>();

        // Two hemispheres: s=0 top (+), s=1 bottom (-)
        for (int s = 0; s < 2; s++)
        {
            float sign = s == 0 ? 1f : -1f;
            for (int i = 0; i <= hemi; i++)
            {
                float theta = Mathf.PI * 0.5f * i / hemi;
                for (int j = 0; j <= segs; j++)
                {
                    float phi = 2f * Mathf.PI * j / segs;
                    float x = r * Mathf.Cos(theta) * Mathf.Cos(phi);
                    float y = sign * (bodyHalf + r * Mathf.Sin(theta));
                    float z = r * Mathf.Cos(theta) * Mathf.Sin(phi);
                    // Rotate based on capsule direction
                    verts.Add(direction switch
                    {
                        0 => new Vector3(y, x, z),  // X-axis
                        2 => new Vector3(x, z, y),  // Z-axis
                        _ => new Vector3(x, y, z),  // Y-axis (default)
                    });
                }
            }
        }

        int w   = segs + 1;
        int tot = (hemi + 1) * w;
        for (int s = 0; s < 2; s++)
        {
            int off = s * tot;
            for (int i = 0; i < hemi; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = off+i*w+j, b = a+w, c = a+1, d = b+1;
                    if (s == 0) tris.AddRange(new[]{ a, b, c, b, d, c });
                    else        tris.AddRange(new[]{ a, c, b, b, c, d });
                }
        }
        // Cylinder body band connecting the two hemispheres
        int top = hemi * w;
        int bot = tot + hemi * w;
        for (int j = 0; j < segs; j++)
        {
            int a = top+j, b = top+j+1, c = bot+j, d = bot+j+1;
            tris.AddRange(new[]{ a, c, b, b, c, d });
        }

        var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
        m.RecalculateNormals();
        return m;
    }

    // ─── Editor Gizmos ────────────────────────────────────────────────────
#if UNITY_EDITOR
    private void OnDrawGizmos()         { if ( alwaysShowGizmo) DrawGizmo(); }
    private void OnDrawGizmosSelected() { if (!alwaysShowGizmo) DrawGizmo(); }

    private void DrawGizmo()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;
        Color solid = gizmoColor;
        Color wire  = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        switch (col)
        {
            case BoxCollider box:
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(box.center),
                    transform.rotation,
                    transform.lossyScale);
                Gizmos.color = solid; Gizmos.DrawCube(Vector3.zero, box.size);
                Gizmos.color = wire;  Gizmos.DrawWireCube(Vector3.zero, box.size);
                Gizmos.matrix = Matrix4x4.identity;
                break;
            case SphereCollider sp:
            {
                float rr = sp.radius * Mathf.Max(
                    transform.lossyScale.x,
                    transform.lossyScale.y,
                    transform.lossyScale.z);
                Vector3 wc = transform.TransformPoint(sp.center);
                Gizmos.color = solid; Gizmos.DrawSphere(wc, rr);
                Gizmos.color = wire;  Gizmos.DrawWireSphere(wc, rr);
                break;
            }
            case CapsuleCollider cap:
            {
                Vector3 wc = transform.TransformPoint(cap.center);
                float   cr = cap.radius * Mathf.Max(
                    transform.lossyScale.x,
                    transform.lossyScale.z);
                Gizmos.color = solid; Gizmos.DrawSphere(wc, cr);
                Gizmos.color = wire;  Gizmos.DrawWireSphere(wc, cr);
                break;
            }
            default:
                Gizmos.color = wire;
                Gizmos.DrawWireSphere(transform.position, 0.3f);
                break;
        }
    }
#endif
}
