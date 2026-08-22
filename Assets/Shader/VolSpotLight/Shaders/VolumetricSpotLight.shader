Shader "Custom/VolumetricSpotLight"
{
    Properties
    {
        _LightColor     ("Light Color",          Color)               = (1, 0.9, 0.7, 1)
        _Intensity      ("Intensity",            Float)               = 2.0
        _Density        ("Density",              Range(0.01, 5.0))    = 0.5
        _MarchSteps     ("March Steps",          Range(8, 128))       = 48
        _DistanceFalloff("Distance Falloff",     Range(0.0, 4.0))     = 1.0
        _SpotFalloff    ("Spot Falloff",         Range(0.1, 8.0))     = 2.0
        _NoiseScale     ("Noise Scale",          Float)               = 2.0
        _NoiseStrength  ("Noise Strength",       Range(0.0, 1.0))     = 0.2
        _NoiseSpeed     ("Noise Speed",          Float)               = 0.3
        _NoiseTex       ("Noise Texture",        2D)                  = "white" {}
        _SoftDepthFade  ("Soft Depth Fade",      Range(0.0, 5.0))     = 0.5
        [Header(Dust Particle Shadow)]
        [Space(5)]
        _DustScale     ("    Particle Scale",  Range(0.5, 15.0))    = 4.0
        _DustSpeed     ("    Drift Speed",     Range(0.0,  3.0))    = 0.5
        _DustDriftX    ("    Drift X",         Range(-1.0, 1.0))    = 0.3
        _DustDriftY    ("    Drift Y",         Range(-1.0, 1.0))    = 0.0
        _DustThreshold ("    Shadow Radius",   Range(0.0,  1.0))    = 0.4
        _DustOpacity   ("    Shadow Opacity",  Range(0.0,  1.0))    = 0.7
        // Set by VolumetricSpotLightController at runtime
        _ConeHalfAngle  ("Cone Half Angle (deg)",Float)               = 30.0
        _ConeLength     ("Cone Length",          Float)               = 5.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+1"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // Additive blending: light volumes accumulate brightness
        Blend One One
        ZWrite Off
        // Render back faces so fragments are always produced whether
        // the camera is inside or outside the cone volume.
        Cull Front
        ZTest LEqual

        Pass
        {
            Name "VolumetricSpotLight"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ---------------------------------------------------------------
            // Input / output structs
            // ---------------------------------------------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---------------------------------------------------------------
            // Material constants
            // ---------------------------------------------------------------
            CBUFFER_START(UnityPerMaterial)
                half4  _LightColor;
                float  _Intensity;
                float  _Density;
                float  _MarchSteps;
                float  _DistanceFalloff;
                float  _SpotFalloff;
                float  _NoiseScale;
                float  _NoiseStrength;
                float  _NoiseSpeed;
                float4 _NoiseTex_ST;
                float  _SoftDepthFade;
                float  _DustScale;
                float  _DustSpeed;
                float  _DustDriftX;
                float  _DustDriftY;
                float  _DustThreshold;
                float  _DustOpacity;
                float  _ConeHalfAngle;
                float  _ConeLength;
            CBUFFER_END

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // ---------------------------------------------------------------
            // Vertex shader
            // ---------------------------------------------------------------
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                return output;
            }

            // ---------------------------------------------------------------
            // Noise helpers
            // ---------------------------------------------------------------
            float Hash(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(Hash(i),                Hash(i+float3(1,0,0)), f.x),
                         lerp(Hash(i+float3(0,1,0)),  Hash(i+float3(1,1,0)), f.x), f.y),
                    lerp(lerp(Hash(i+float3(0,0,1)),  Hash(i+float3(1,0,1)), f.x),
                         lerp(Hash(i+float3(0,1,1)),  Hash(i+float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            float FBM(float3 p)
            {
                return ValueNoise(p)               * 0.500
                     + ValueNoise(p * 2.0 + 100.0) * 0.250
                     + ValueNoise(p * 4.0 + 200.0) * 0.125;
            }

            // ---------------------------------------------------------------
            // Worley (cellular) noise — used for pseudo dust-particle shadows
            // Returns distance to nearest random cell point, scaled to [0, 1].
            // Low value = near a "particle centre" → shadow region.
            // ---------------------------------------------------------------
            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453123);
            }

            float WorleyNoise(float2 p)
            {
                float2 cell    = floor(p);
                float  minDist = 1e10;

                UNITY_UNROLL
                for (int j = -1; j <= 1; j++)
                UNITY_UNROLL
                for (int i = -1; i <= 1; i++)
                {
                    float2 nb        = cell + float2(i, j);
                    float2 cellPoint = nb + Hash2(nb);  // random point inside cell
                    minDist = min(minDist, length(p - cellPoint));
                }
                // Typical max ≈ 0.7 for unit cells → scale to [0, 1]
                return saturate(minDist * 1.4142);
            }

            // ---------------------------------------------------------------
            // Cone ray intersection  (direct port of Base.txt → HLSL)
            //
            // Cone definition: apex at object-space origin, opens along +Z.
            // At depth z, cone radius = z * tanHalfAngle.
            // Valid region: 0 ≤ z ≤ coneLength.
            //
            // Returns true when the ray intersects the bounded cone.
            // tNear / tFar are the entry and exit parameters along `rd`.
            // ---------------------------------------------------------------
            bool IntersectCone(float3 ro, float3 rd,
                               float tanHalfAngle, float coneLength,
                               out float tNear, out float tFar)
            {
                tNear = 0.0;
                tFar  = 0.0;

                float k = tanHalfAngle * tanHalfAngle;

                // Cone surface equation: x² + y² − k·z² = 0
                float a =  rd.x*rd.x + rd.y*rd.y - k*rd.z*rd.z;
                float b =  ro.x*rd.x + ro.y*rd.y - k*ro.z*rd.z;
                float c =  ro.x*ro.x + ro.y*ro.y - k*ro.z*ro.z;

                // Parameter where the ray hits the flat cap at z = coneLength
                float tCap = (abs(rd.z) > 1e-5) ? (coneLength - ro.z) / rd.z : 1e10;

                // --- Linear case (ray nearly parallel to cone surface) ---
                if (abs(a) < 1e-5)
                {
                    if (abs(b) < 1e-5) return false;
                    float t = -0.5 * c / b;
                    float z = ro.z + t * rd.z;
                    if (z < 0.0 || z > coneLength) return false;
                    tNear = min(t, tCap);
                    tFar  = max(t, tCap);
                    return tFar > 0.0;
                }

                // --- Quadratic case: two roots ---
                float delta = b*b - a*c;
                if (delta < 0.0) return false;

                float sq   = sqrt(delta);
                float invA = 1.0 / a;
                float t0   = (-b - sq) * invA;
                float t1   = (-b + sq) * invA;

                tNear = min(t0, t1);
                tFar  = max(t0, t1);

                float zNear = ro.z + tNear * rd.z;
                float zFar  = ro.z + tFar  * rd.z;

                // Clip the segment to the valid cone region 0 ≤ z ≤ coneLength
                if (zNear < 0.0)
                {
                    if (zFar < 0.0 || zFar > coneLength) return false;
                    tNear = tFar;
                    tFar  = tCap;
                }
                else if (zNear > coneLength)
                {
                    if (zFar < 0.0 || zFar > coneLength) return false;
                    tNear = tCap;
                }
                else if (zFar < 0.0)
                {
                    tFar  = tNear;
                    tNear = tCap;
                }
                else if (zFar > coneLength)
                {
                    tFar = tCap;
                }

                // Ensure tNear ≤ tFar after the clipping above
                float tmp = tNear;
                tNear = min(tNear, tFar);
                tFar  = max(tmp,   tFar);

                return tFar > 0.0;
            }

            // ---------------------------------------------------------------
            // Fragment shader
            // ---------------------------------------------------------------
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // --- World-space ray ---
                float3 camWS    = GetCameraPositionWS();
                float3 rayDirWS = normalize(input.positionWS - camWS);

                // --- Transform ray into cone's object space ---
                float4x4 W2O = UNITY_MATRIX_I_M;
                float3 roOS  = mul(W2O, float4(camWS, 1.0)).xyz;
                float3 rdOS  = normalize(mul((float3x3)W2O, rayDirWS));

                float tanHA = tan(radians(_ConeHalfAngle));

                // --- Cone intersection (from Base.txt) ---
                float tNear, tFar;
                if (!IntersectCone(roOS, rdOS, tanHA, _ConeLength, tNear, tFar))
                    return half4(0, 0, 0, 0);

                tNear = max(tNear, 0.0);
                if (tNear >= tFar)
                    return half4(0, 0, 0, 0);

                // --- Depth occlusion + soft intersection fade ---
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float  sceneDepthRaw = SampleSceneDepth(screenUV);

                // Skip depth clip against the sky (reversed-Z: sky ≈ 0)
                #if UNITY_REVERSED_Z
                    bool isSky = sceneDepthRaw < 1e-6;
                #else
                    bool isSky = sceneDepthRaw > 1.0 - 1e-6;
                #endif

                // tScene: ray parameter at the scene surface (object space).
                // Used both for hard clipping and for the per-sample soft fade.
                float tScene = 1e10;
                if (!isSky)
                {
                    float3 sceneWS = ComputeWorldSpacePosition(screenUV, sceneDepthRaw, UNITY_MATRIX_I_VP);
                    float3 sceneOS = mul(W2O, float4(sceneWS, 1.0)).xyz;
                    tScene = dot(sceneOS - roOS, rdOS);
                    tFar   = min(tFar, tScene);
                }

                if (tNear >= tFar)
                    return half4(0, 0, 0, 0);

                // --- Raymarching loop ---
                int   steps    = (int)clamp(_MarchSteps, 8.0, 128.0);
                float stepSize = (tFar - tNear) / float(steps);

                // Sub-pixel jitter to break up banding
                float jitter = Hash(float3(input.positionCS.xy * 0.1, frac(_Time.y))) * stepSize;

                float3 accumColor  = float3(0, 0, 0);
                float  transmit    = 1.0;

                UNITY_LOOP
                for (int i = 0; i < steps; i++)
                {
                    float  t     = tNear + jitter + float(i) * stepSize;
                    if (t >= tFar) break;

                    float3 posOS = roOS + rdOS * t;

                    float z = posOS.z;
                    if (z <= 0.0 || z >= _ConeLength) continue;

                    // Radial position: 0 at axis, 1 at cone surface
                    float coneR    = z * tanHA;
                    float normDist = saturate(length(posOS.xy) / max(coneR, 1e-5));

                    // Angular falloff from cone axis
                    float spotFactor = pow(1.0 - normDist, _SpotFalloff);
                    // Distance falloff from apex toward base
                    float distFactor = saturate(1.0 - pow(z / _ConeLength, _DistanceFalloff));

                    // Procedural FBM + texture noise (RaymarchingSpotLight approach)
                    float noiseVal = 1.0;
                    if (_NoiseStrength > 0.01)
                    {
                        // FBM layer: 3D volumetric detail
                        float3 np      = posOS * _NoiseScale + float3(0, 0, _Time.y * _NoiseSpeed);
                        float  fbmNoise = saturate(FBM(np) * 2.0);

                        // Texture layer: artist-controllable pattern, sampled on XY cross-section
                        float2 noiseUV  = frac(posOS.xy * _NoiseScale + float2(0, _Time.y * _NoiseSpeed));
                        float  texNoise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;

                        // Suppress texture pattern near the apex (too small a cross-section)
                        float apexFade = saturate(z / max(_ConeLength * 0.05, 0.001));
                        texNoise = lerp(1.0, texNoise, apexFade);

                        noiseVal = lerp(1.0, saturate(fbmNoise * texNoise * 1.5), _NoiseStrength);
                    }

                    // Soft depth fade: density → 0 as the sample approaches the scene surface.
                    float depthFade = (_SoftDepthFade > 0.001)
                        ? saturate((tScene - t) / _SoftDepthFade)
                        : 1.0;

                    // Energy conservation: intensity falls as cone cross-section grows.
                    float areaFactor = rsqrt(1.0 + PI * coneR * coneR);

                    // --- Pseudo dust-particle shadow (Worley noise) ---
                    // Two Worley layers at different scales give natural size variation.
                    // The XY cross-section drifts over time to simulate floating dust.
                    float dustFactor = 1.0;
                    if (_DustOpacity > 0.01)
                    {
                        // Angular UV: divide by cone radius at this depth so shadows
                        // radiate from the apex rather than running parallel to Z.
                        // angularPos = (0,0) on axis, magnitude 1 at cone edge.
                        float2 angularPos = posOS.xy / max(coneR, 1e-4);

                        float2 drift = float2(_DustDriftX, _DustDriftY) * _Time.y * _DustSpeed;
                        float2 uv1   = angularPos * _DustScale       + drift;
                        float2 uv2   = angularPos * _DustScale * 2.3 + drift * 1.1 + float2(3.1, 7.4);

                        // Take the closer cell across both layers
                        float cell   = min(WorleyNoise(uv1), WorleyNoise(uv2) * 0.9);

                        // smoothstep: 0 near cell centre (shadow) → 1 away (lit)
                        float shadow = smoothstep(0.0, _DustThreshold, cell);
                        dustFactor   = lerp(1.0 - _DustOpacity, 1.0, shadow);
                    }

                    float density = max(0.0, _Density * spotFactor * distFactor * areaFactor * noiseVal * dustFactor * depthFade);
                    float extinction = density * stepSize;

                    // Beer-Lambert: in-scattering weighted by current transmittance
                    accumColor += _LightColor.rgb * _Intensity * density * stepSize * transmit;
                    transmit   *= exp(-extinction);

                    if (transmit < 0.005) break;
                }

                // Near-camera fade: dissolve when the cone mesh face is right at the camera
                // (prevents hard pop at mesh boundaries when camera grazes the surface).
                float camDist = length(input.positionWS - camWS);
                return half4(accumColor, 1.0 - transmit) * saturate(camDist);
            }

            ENDHLSL
        }
    }
    FallBack Off
}
