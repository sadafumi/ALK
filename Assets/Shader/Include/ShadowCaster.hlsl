#ifndef CHARACTER_SHADOW_CASTER_PASS_INCLUDED
#define CHARACTER_SHADOW_CASTER_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#if defined(LOD_FADE_CROSSFADE)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

float3 _LightDirection;
float3 _LightPosition;

struct Attributes
{
    float4 vertex : POSITION;
    float4 normal : NORMAL;
    float4 tangent : TANGENT;
    float2 texcoord : TEXCOORD0;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
#if defined(_ALPHATEST_ON)
        float2 uv       : TEXCOORD0;
#endif
    float4 vertex : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float3 ViewDirectionOS(float3 vertex)
{
    return TransformWorldToObject(_WorldSpaceCameraPos).xyz - vertex;
}
float GetOutlineWidth(float3 vertex, float3 positionWS, float outlineWidth, uint outlineVertexR2Width, float outlineFixWidth)
{
    outlineWidth *= 0.01;
    outlineWidth *= lerp(1.0, saturate(length(ViewDirection(positionWS))), outlineFixWidth);
    return outlineWidth;
}
float3 ToAbsolutePositionWS(float3 positionRWS)
{
    return positionRWS + _WorldSpaceCameraPos.xyz;

}
float4 OptMul(float4x4 mat, float3 pos)
{
    return mat._m00_m10_m20_m30 * pos.x + (mat._m01_m11_m21_m31 * pos.y + (mat._m02_m12_m22_m32 * pos.z + mat._m03_m13_m23_m33));
}
float3 CalcOutlinePosition(inout float3 vertex, float3 normalOS, float3x3 tbnOS, float outlineWidth, uint outlineVertexR2Width, float outlineFixWidth, float outlineZBias)
{
    float3 positionWS = ToAbsolutePositionWS(OptMul(GetObjectToWorldMatrix(), vertex).xyz);
    float width = GetOutlineWidth(vertex, positionWS, outlineWidth, outlineVertexR2Width, outlineFixWidth);
    float3 outlineN = normalOS;

    if (outlineVertexR2Width == 2)
        outlineN = mul(float3(1, 1, 1) * 2.0 - 1.0, tbnOS);
    vertex += outlineN * width;
    float3 V = ViewDirectionOS(vertex);
    vertex -= normalize(V) * outlineZBias;
    return width;
}
float4 GetShadowPositionHClip(Attributes input)
{
    float3 positionWS = TransformObjectToWorld(input.vertex.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normal);

#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
#else
    float3 lightDirectionWS = _LightDirection;
#endif

    float4 vertex = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

#if UNITY_REVERSED_Z
    vertex.z = min(vertex.z, UNITY_NEAR_CLIP_VALUE);
#else
    vertex.z = max(vertex.z, UNITY_NEAR_CLIP_VALUE);
#endif

    return vertex;
}

Varyings ShadowPassVertex(Attributes input)
{
    Varyings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float4 vertex_pos = input.vertex;
#if defined(_ALPHATEST_ON)
        output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
#endif
    float3 normal = input.normal.xyz;

    normal = input.tangent.xyz;
    
    float4 tangent = input.tangent;

    float3 tangent_os = normalize(cross(normal, tangent.xyz)) * (tangent.w * length(normal));
    float3x3 tbnOS = float3x3(input.tangent.xyz, tangent_os, input.normal.xyz);
         
    output.vertex = GetShadowPositionHClip(input);
    return output;
}

half4 ShadowPassFragment(Varyings input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);

#if defined(_ALPHATEST_ON)
        Alpha(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_MainTex, sampler_MainTex)).a, _MainColor, _Cutoff);
#endif

#if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(input.vertex);
#endif

    return 0;
}

#endif
