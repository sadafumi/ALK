Shader "Hidden/PCSS/ScreenSpaceShadows"
{
    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True"}

        HLSLINCLUDE

        //Keep compiler quiet about Shadows.hlsl.
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
        // Core.hlsl for XR dependencies
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        // x: penumbra scale (2 * tan(lightAngularDiameter / 2))
        // y: min penumbra width (world units)
        // z: max penumbra width (world units)
        // w: blocker search radius (world units)
        float4 _PCSSParams0;
        // x: blocker sample count
        // y: filter sample count
        // z: blocker depth bias (shadowmap depth units)
        // w: main light cascade count (1, 2 or 4)
        float4 _PCSSParams1;

        #define GOLDEN_ANGLE 2.39996323

        float2 PCSS_VogelDiskSample(int sampleIndex, float invSampleCount, float phi)
        {
            float r = sqrt((sampleIndex + 0.5) * invSampleCount);
            float theta = sampleIndex * GOLDEN_ANGLE + phi;
            float s, c;
            sincos(theta, s, c);
            return r * float2(c, s);
        }

        // Receiver plane depth bias (Isidoro): derive the shadow-space depth gradient
        // of the surface from screen-space derivatives, so each kernel sample compares
        // against the depth the surface itself is expected to have at that offset.
        // Without this, sloped surfaces (e.g. the ground at a grazing light angle)
        // register their own neighbouring texels as blockers, which shows up as a
        // grey self-shadow disc covering exactly the cascade-0 split sphere.
        float2 PCSS_ReceiverPlaneDepthGradient(float3 duvz_dx, float3 duvz_dy)
        {
            float det = duvz_dx.x * duvz_dy.y - duvz_dx.y * duvz_dy.x;
            if (abs(det) < 1e-12)
                return float2(0.0, 0.0);
            float2 dz_duv = float2(
                duvz_dy.y * duvz_dx.z - duvz_dx.y * duvz_dy.z,
                duvz_dx.x * duvz_dy.z - duvz_dy.x * duvz_dx.z);
            return dz_duv / det;
        }

        // Derivatives explode across depth discontinuities and cascade seams inside
        // a 2x2 quad, so the per-sample correction is clamped to a small depth range.
        #define PCSS_MAX_PLANE_BIAS 0.05

        float PCSS_ExpectedDepth(float z, float2 dz_duv, float2 offsetUV)
        {
            return z + clamp(dot(dz_duv, offsetUV), -PCSS_MAX_PLANE_BIAS, PCSS_MAX_PLANE_BIAS);
        }

        // The cascade atlas packs up to 4 tiles into one texture. Wide PCSS kernels
        // must not read across tile borders, so clamp UVs to the tile that contains uvCenter.
        void PCSS_GetTileBounds(float2 uvCenter, out float2 tileMin, out float2 tileMax)
        {
            float cascadeCount = _PCSSParams1.w;
            float2 grid = float2(cascadeCount > 1.5 ? 2.0 : 1.0, cascadeCount > 2.5 ? 2.0 : 1.0);
            float2 tileSize = 1.0 / grid;
            float2 tileId = clamp(floor(uvCenter * grid), 0.0, grid - 1.0);
            float2 texel = _MainLightShadowmapSize.xy;
            tileMin = tileId * tileSize + texel;
            tileMax = (tileId + 1.0) * tileSize - texel;
        }

        half4 Fragment(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float deviceDepth = LoadSceneDepth(input.positionCS.xy);
        #if !UNITY_REVERSED_Z
            deviceDepth = deviceDepth * 2.0 - 1.0;
        #endif

            float3 wpos = ComputeWorldSpacePosition(input.texcoord.xy, deviceDepth, unity_MatrixInvVP);

        #ifdef _MAIN_LIGHT_SHADOWS_CASCADE
            half cascadeIndex = ComputeCascadeIndex(wpos);
        #else
            half cascadeIndex = half(0.0);
        #endif
            float4x4 shadowMatrix = _MainLightWorldToShadow[cascadeIndex];
            float3 coord = mul(shadowMatrix, float4(wpos, 1.0)).xyz;

            // Derivatives must be computed in uniform control flow, before any branch.
            // Take them in world space (continuous across cascade boundaries) and
            // transform by this pixel's own cascade matrix; taking ddx/ddy of `coord`
            // directly produces garbage where a 2x2 quad straddles two cascades,
            // which shows up as a dark dashed line along the cascade seam.
            float3 dwpos_dx = ddx(wpos);
            float3 dwpos_dy = ddy(wpos);
            float3 duvz_dx = mul((float3x3)shadowMatrix, dwpos_dx);
            float3 duvz_dy = mul((float3x3)shadowMatrix, dwpos_dy);
            float2 dz_duv = PCSS_ReceiverPlaneDepthGradient(duvz_dx, duvz_dy);

            if (BEYOND_SHADOW_FAR(coord))
                return half4(1, 1, 1, 1);

            half4 shadowParams = GetMainLightShadowParams();

            // Directional shadow projection is orthographic, so the rows of the
            // world-to-shadow matrix directly give the world-to-UV / world-to-depth
            // scales of the current cascade. This keeps the penumbra size consistent
            // in world units across cascades.
            float worldToUV = length(shadowMatrix[0].xyz);
            float worldToDepth = max(length(shadowMatrix[2].xyz), 1e-6);

            float2 tileMin, tileMax;
            PCSS_GetTileBounds(coord.xy, tileMin, tileMax);

            float noise = InterleavedGradientNoise(input.positionCS.xy, 0) * TWO_PI;

            // ---- 1) Blocker search -------------------------------------------
            float searchRadiusUV = _PCSSParams0.w * worldToUV;
            int blockerSamples = (int)_PCSSParams1.x;
            float invBlockerSamples = rcp((float)blockerSamples);
            float blockerBias = _PCSSParams1.z;

            float z = coord.z;
            float blockerDiffSum = 0.0;
            float blockerCount = 0.0;

            [loop]
            for (int i = 0; i < blockerSamples; ++i)
            {
                float2 offset = PCSS_VogelDiskSample(i, invBlockerSamples, noise) * searchRadiusUV;
                float2 uv = clamp(coord.xy + offset, tileMin, tileMax);
                float zExpected = PCSS_ExpectedDepth(z, dz_duv, uv - coord.xy);
                float d = SAMPLE_TEXTURE2D_LOD(_MainLightShadowmapTexture, sampler_PointClamp, uv, 0).r;
        #if UNITY_REVERSED_Z
                float diff = d - zExpected;
        #else
                float diff = zExpected - d;
        #endif
                if (diff > blockerBias)
                {
                    blockerDiffSum += diff;
                    blockerCount += 1.0;
                }
            }

            // Nothing between this pixel and the light: fully lit.
            if (blockerCount < 0.5)
                return half4(1, 1, 1, 1);

            // ---- 2) Penumbra estimation --------------------------------------
            float depthDiff = blockerDiffSum / blockerCount;
            float depthDiffWS = depthDiff / worldToDepth;
            float penumbraWS = clamp(depthDiffWS * _PCSSParams0.x + _PCSSParams0.y, _PCSSParams0.y, _PCSSParams0.z);
            float filterRadiusUV = max(penumbraWS * worldToUV, _MainLightShadowmapSize.x);

            // ---- 3) Variable-radius PCF --------------------------------------
            int filterSamples = (int)_PCSSParams1.y;
            float invFilterSamples = rcp((float)filterSamples);
            real attenuation = 0.0;

            [loop]
            for (int j = 0; j < filterSamples; ++j)
            {
                float2 offset = PCSS_VogelDiskSample(j, invFilterSamples, noise + 1.618) * filterRadiusUV;
                float2 uv = clamp(coord.xy + offset, tileMin, tileMax);
                float zExpected = PCSS_ExpectedDepth(z, dz_duv, uv - coord.xy);
                attenuation += real(SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, float3(uv, zExpected)));
            }
            attenuation *= invFilterSamples;

            return LerpWhiteTo(attenuation, shadowParams.x);
        }

        ENDHLSL

        Pass
        {
            Name "PCSS ScreenSpaceShadows"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma multi_compile _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #pragma vertex   Vert
            #pragma fragment Fragment
            ENDHLSL
        }
    }
}
