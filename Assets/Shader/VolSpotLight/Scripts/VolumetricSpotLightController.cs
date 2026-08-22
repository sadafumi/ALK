using UnityEngine;

/// <summary>
/// Attaches to a GameObject and creates a cone mesh + material for the
/// Custom/VolumetricSpotLight shader.
///
/// Setup:
///   1. Add this component to an empty GameObject.
///   2. Assign the shader "Custom/VolumetricSpotLight" (via Inspector or auto-detected).
///   3. Optionally link a Unity SpotLight to 'sourceLight' to sync parameters automatically.
///   4. In the URP Renderer asset, enable "Depth Texture" so the occlusion sampling works.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[ExecuteInEditMode]
public class VolumetricSpotLightController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------
    [Header("Light Parameters")]
    public Color  lightColor      = new Color(1f, 0.9f, 0.7f, 1f);
    public float  intensity       = 2f;
    [Range(1f, 89f)]
    public float  halfAngle       = 30f;   // degrees
    public float  coneLength      = 5f;

    [Header("Volume Parameters")]
    [Range(0.01f, 5f)]
    public float  density         = 0.5f;
    [Range(8, 128)]
    public int    marchSteps      = 48;
    [Range(0f, 4f)]
    public float  distanceFalloff = 1f;
    [Range(0.1f, 8f)]
    public float  spotFalloff     = 2f;
    [Range(0f, 5f)]
    public float  softDepthFade   = 0.5f;

    [Header("Dust Particle Shadow")]
    [Range(0.5f, 15f)]
    public float  dustScale       = 4.0f;
    [Range(0f, 3f)]
    public float  dustSpeed       = 0.5f;
    [Range(-1f, 1f)]
    public float  dustDriftX      = 0.3f;
    [Range(-1f, 1f)]
    public float  dustDriftY      = 0.0f;
    [Range(0f, 1f)]
    public float  dustThreshold   = 0.4f;
    [Range(0f, 1f)]
    public float  dustOpacity     = 0.7f;

    [Header("Noise (dust / fog look)")]
    public float    noiseScale      = 2f;
    [Range(0f, 1f)]
    public float    noiseStrength   = 0.2f;
    public float    noiseSpeed      = 0.3f;
    public Texture2D noiseTex       = null;  // optional; leave null for procedural FBM only

    [Header("Sync with Unity SpotLight (optional)")]
    public Light  sourceLight;

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------
    MeshFilter   _mf;
    MeshRenderer _mr;
    Material     _mat;

    float _cachedAngle  = -1f;
    float _cachedLength = -1f;

    // -------------------------------------------------------------------------
    // Unity callbacks
    // -------------------------------------------------------------------------
    void Awake()
    {
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
    }

    void OnEnable()
    {
        EnsureMaterial();
        RebuildMesh();
        PushToMaterial();
    }

    void Update()
    {
        if (sourceLight != null)
            PullFromLight();

        if (!Mathf.Approximately(halfAngle, _cachedAngle) ||
            !Mathf.Approximately(coneLength, _cachedLength))
        {
            RebuildMesh();
        }

        PushToMaterial();
    }

    void OnValidate()
    {
        // Called in editor when inspector values change
        _mf = GetComponent<MeshFilter>();
        _mr = GetComponent<MeshRenderer>();
        EnsureMaterial();
        RebuildMesh();
        PushToMaterial();
    }

    void OnDestroy()
    {
        DestroyMaterial();
    }

    void OnDisable()
    {
        DestroyMaterial();
    }

    // -------------------------------------------------------------------------
    // Sync with Unity SpotLight
    // -------------------------------------------------------------------------
    void PullFromLight()
    {
        lightColor  = sourceLight.color;
        intensity   = sourceLight.intensity;
        halfAngle   = sourceLight.spotAngle * 0.5f;
        coneLength  = sourceLight.range;
    }

    // -------------------------------------------------------------------------
    // Material management
    // -------------------------------------------------------------------------
    void EnsureMaterial()
    {
        if (_mat != null) return;

        Shader shader = Shader.Find("Custom/VolumetricSpotLight");
        if (shader == null)
        {
            Debug.LogError("[VolumetricSpotLight] Shader 'Custom/VolumetricSpotLight' not found. " +
                           "Make sure VolumetricSpotLight.shader is in the project.");
            return;
        }

        _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        if (_mr != null)
            _mr.sharedMaterial = _mat;
    }

    void PushToMaterial()
    {
        if (_mat == null) return;

        _mat.SetColor ("_LightColor",      lightColor);
        _mat.SetFloat ("_Intensity",       intensity);
        _mat.SetFloat ("_Density",         density);
        _mat.SetFloat ("_MarchSteps",      marchSteps);
        _mat.SetFloat ("_DistanceFalloff", distanceFalloff);
        _mat.SetFloat ("_SpotFalloff",     spotFalloff);
        _mat.SetFloat ("_SoftDepthFade",   softDepthFade);
        _mat.SetFloat ("_DustScale",       dustScale);
        _mat.SetFloat ("_DustSpeed",       dustSpeed);
        _mat.SetFloat ("_DustDriftX",      dustDriftX);
        _mat.SetFloat ("_DustDriftY",      dustDriftY);
        _mat.SetFloat ("_DustThreshold",   dustThreshold);
        _mat.SetFloat ("_DustOpacity",     dustOpacity);
        _mat.SetFloat   ("_NoiseScale",    noiseScale);
        _mat.SetFloat   ("_NoiseStrength", noiseStrength);
        _mat.SetFloat   ("_NoiseSpeed",    noiseSpeed);
        _mat.SetTexture ("_NoiseTex",      noiseTex);
        _mat.SetFloat ("_ConeHalfAngle",   halfAngle);
        _mat.SetFloat ("_ConeLength",      coneLength);
    }

    void DestroyMaterial()
    {
        if (_mat == null) return;
        if (Application.isPlaying)
            Destroy(_mat);
        else
            DestroyImmediate(_mat);
        _mat = null;
    }

    // -------------------------------------------------------------------------
    // Cone mesh generation
    //
    // Apex   : object-space origin (0, 0, 0)
    // Opening: +Z axis
    // Radius at z=coneLength: coneLength * tan(halfAngle)
    //
    // The mesh is slightly oversized (2 %) to prevent edge-clipping artefacts.
    // -------------------------------------------------------------------------
    void RebuildMesh()
    {
        _cachedAngle  = halfAngle;
        _cachedLength = coneLength;

        if (_mf == null) return;

        _mf.sharedMesh = BuildConeMesh(halfAngle, coneLength, segmentCount: 32);
    }

    static Mesh BuildConeMesh(float halfAngleDeg, float length, int segmentCount)
    {
        const float oversize = 1.02f;
        float radius = length * Mathf.Tan(halfAngleDeg * Mathf.Deg2Rad) * oversize;
        float len    = length * oversize;

        // Vertices: apex (0) + ring (1..N) + cap centre (N+1)
        int vertCount = segmentCount + 2;
        var vertices  = new Vector3[vertCount];

        vertices[0] = Vector3.zero;                    // apex

        for (int i = 0; i < segmentCount; i++)
        {
            float a = (float)i / segmentCount * Mathf.PI * 2f;
            vertices[1 + i] = new Vector3(Mathf.Cos(a) * radius,
                                          Mathf.Sin(a) * radius,
                                          len);
        }

        vertices[segmentCount + 1] = new Vector3(0f, 0f, len); // cap centre

        // Triangles: lateral surface + base cap
        var tris = new int[segmentCount * 3 * 2];
        int idx  = 0;

        for (int i = 0; i < segmentCount; i++)
        {
            int next = (i + 1) % segmentCount;
            // Lateral face (winding: back-face toward outside)
            tris[idx++] = 0;
            tris[idx++] = 1 + next;
            tris[idx++] = 1 + i;
        }

        for (int i = 0; i < segmentCount; i++)
        {
            int next = (i + 1) % segmentCount;
            // Base cap face
            tris[idx++] = segmentCount + 1;
            tris[idx++] = 1 + i;
            tris[idx++] = 1 + next;
        }

        var mesh = new Mesh { name = "VolumetricConeMesh" };
        mesh.vertices  = vertices;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
