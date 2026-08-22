#ifndef LIGHTDIRECTION
#define LIGHTDIRECTION

float3 LightDirection(float3 pos_w)
{
    float4 shadow_coord = TransformWorldToShadowCoord(pos_w);
    Light mainLight = GetMainLight(shadow_coord);
    
    
    return mainLight.direction;
}
float3 LightDirection(float3 pos_w, out float3 color)
{
    float4 shadow_coord = TransformWorldToShadowCoord(pos_w);
    Light mainLight = GetMainLight(shadow_coord);
    
    color = mainLight.color;
    
    return mainLight.direction;
}

#endif
