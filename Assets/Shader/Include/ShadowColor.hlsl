#ifndef SHADOWCOLOR
#define SHADOWCOLOR

TEXTURE2D(_ShadowMap);
SAMPLER(sampler_ShadowMap);

TEXTURE2D(_ShadowRampMap);
SAMPLER(sampler_ShadowRampMap);

float _ShadingAreaCorrection;

float3 ShadowColor(float3 color, float2 uv, float4 pack1, float3 pos_w, float3 v_light, float3 normal_w, out float shadowMask)
{

    float4 shadow_color = SAMPLE_TEXTURE2D(_ShadowMap, sampler_ShadowMap, uv);
    
    float skinMask = shadow_color.a;

    float occlusion = pack1.r + 0.5;
    float3 lightDir = normalize(v_light);
    
    //shadowMask = 1;
    //return occlusion;
    
    float shadowAtten = saturate((dot(normal_w, lightDir) + 1) / 2);
    shadowAtten = shadowAtten * occlusion;
    
    float4 shadowramp = SAMPLE_TEXTURE2D(_ShadowRampMap, sampler_ShadowRampMap, float2(shadowAtten + _ShadingAreaCorrection, 0));
    shadowAtten = shadowramp.a;
    shadowMask = shadowAtten;
    shadow_color.rgb = ((shadow_color.rgb * (1 - skinMask)) + (color.rgb * shadowramp.rgb) * skinMask);
    
    color.rgb = lerp(shadow_color.rgb , color.rgb , shadowAtten);
    return color;
}

#endif