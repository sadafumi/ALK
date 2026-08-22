#ifndef OVERLAY
#define OVERLAY

float3 OverlaysRGB(float3 color_base, float3 color_dis)
{
    color_base = LinearToSRGB(color_base);
    color_dis = LinearToSRGB(color_dis);
    return SRGBToLinear(lerp(2.0 * color_base * color_dis, 1.0 - 2.0 * (1.0 - color_base) * (1.0 - color_dis), step(0.5, color_base)));
    //return lerp(2.0 * color_base * color_dis, 1.0 - 2.0 * (1.0 - color_base) * (1.0 - color_dis), step(color_base, 0.5));
}
float3 Overlay(float3 color_base, float3 color_dis)
{
    return lerp(2.0 * color_base * color_dis, 1.0 - 2.0 * (1.0 - color_base) * (1.0 - color_dis), step(0.5, color_base));
    //return lerp(2.0 * color_base * color_dis, 1.0 - 2.0 * (1.0 - color_base) * (1.0 - color_dis), step(color_base, 0.5));
}

#endif