#ifndef RIMLIGHT
#define RIMLIGHT

#include "Assets/Shader/Include/Overlay.hlsl"
#include "Assets/Shader/Include/Remap.hlsl"


float4 _RimLightColor;
float _RimThreshold;
float _RimSmooth;
float _RimScale;


TEXTURE2D(_RimLightMaskTex);
SAMPLER(sampler_RimLightMaskTex);

float3 RimLight(float3 color, float2 uv, float4 pack1, float3 normal_w, float3 v_view, float3 v_light)
{
    float4 rimlightMask = SAMPLE_TEXTURE2D(_RimLightMaskTex, sampler_RimLightMaskTex, uv);
    float LdotN = dot(normal_w, v_light) * _RimScale;
    
    float fresnel = saturate(dot(normal_w, normalize(v_view)));
	float rim = 1.0 - fresnel;
    rim = smoothstep(_RimThreshold, _RimThreshold + _RimSmooth, rim + LdotN + (pack1.r - 0.5)) * rimlightMask.rgb;
    
    color = lerp(color, color + (_RimLightColor.rgb), rim * _RimLightColor.a );
    
    return color;
}

#endif