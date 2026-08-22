#ifndef VIEWDIRECTION
#define VIEWDIRECTION

float3 ViewDirection(float3 pos_w)
{
    return normalize(_WorldSpaceCameraPos.xyz - pos_w.xyz);
}

#endif