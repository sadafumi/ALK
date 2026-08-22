#ifndef RAMP
#define RAMP

#define GS_BLEND_COUNT 6

float _EdgeEnd;

int GS_DecodeBlendMode(float a)
{
    int m = (int) floor(saturate(a) * GS_BLEND_COUNT);
    return clamp(m, 0, GS_BLEND_COUNT - 1);
}

float3 GS_Blend_Add(float3 base, float3 src)
{
    return base + src;
}
float3 GS_Blend_Multiply(float3 base, float3 src)
{
    return base * src;
}
float3 GS_Blend_Overlay(float3 base, float3 src)
{
    float3 low = 2.0 * base * src;
    float3 high = 1.0 - 2.0 * (1.0 - base) * (1.0 - src);
    float3 sel = step(0.5, base); // base>=0.5 Å® 1
    return lerp(low, high, sel);
}

float3 GS_Blend_Reserved4(float3 base, float3 src)
{
    return src;
} 
float3 GS_Blend_Reserved5(float3 base, float3 src)
{
    return src;
} 
float3 GS_Blend_Reserved6(float3 base, float3 src)
{
    return src;
} 

float3 GS_ApplyBlend(int mode, float3 base, float3 src)
{
    if (mode == 0)
        return GS_Blend_Add(base, src);
    if (mode == 1)
        return GS_Blend_Multiply(base, src);
    if (mode == 2)
        return GS_Blend_Overlay(base, src);
    if (mode == 3)
        return GS_Blend_Reserved4(base, src);
    if (mode == 4)
        return GS_Blend_Reserved5(base, src);
    return GS_Blend_Reserved6(base, src);
}

float3 GS_ApplyBlendMasked(int mode, float3 base, float3 src, float mask)
{
    float m = saturate(mask);
    
    if (m <= 0.0)
        return base;
    float3 blended = GS_ApplyBlend(mode, base, src);
    return lerp(base, blended, m);
}


float3 Ramp(float3 color, float2 uv, float4 pack1, float4 pack2, float3 normal_w, float3 v_view, inout float3 debug_ramp)
{
	float ndv = clamp(dot(normal_w, v_view), 1e-5, 1.0);

	float diffuse = sqrt(saturate(1.0 - ndv * ndv));   
	diffuse = diffuse / _EdgeEnd;                    
	diffuse -= (pack1.r - 0.5);        
	diffuse = saturate(diffuse);       
        
    float ramp_mask = SAMPLE_TEXTURE2D(_RampMaskTex, sampler_RampMaskTex, uv).r;
    float4 ramp = SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(diffuse, 1 - pack2.r));
    //ramp *= pack1.r;
    
    int mode = GS_DecodeBlendMode(ramp.a);
    color.rgb = GS_ApplyBlendMasked(mode, color.rgb, ramp.rgb, ramp_mask /** pack1.r*/);
    //color.rgb += ramp.rgb * ramp_mask;
    //color.rgb = ramp.rgb * ramp_mask;

    debug_ramp = GS_ApplyBlendMasked(mode, color.rgb, ramp.rgb, ramp_mask * pack1.r);
    return color;
}

#endif