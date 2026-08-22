#ifndef CHARACTER_INPORT_INCLUDED
#define CHARACTER_INPORT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ParallaxMapping.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"


struct in_vert
{
    float4 vertex : POSITION;
    float4 color : COLOR;
    float2 uv : TEXCOORD0;
    float4 normal : NORMAL;
    float4 tangent : TANGENT;

};
struct in_frag
{
    float4 vertex : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;
    float3 normal : TEXCOORD2;
    float3 normal_w : TEXCOORD3;
    float4 tangent : TEXCOORD4;
    float3 pos_w : TEXCOORD5;

};


TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

TEXTURE2D(_Packing1Tex);
SAMPLER(sampler_Packing1Tex);

TEXTURE2D(_Packing2Tex);
SAMPLER(sampler_Packing2Tex);

TEXTURE2D(_RampTex);
SAMPLER(sampler_RampTex);

TEXTURE2D(_RampMaskTex);
SAMPLER(sampler_RampMaskTex);


CBUFFER_START(UnityPerMaterial)
#include "Assets/Shader/Include/AlphaTest.hlsl"

#include "Assets/Shader/Include/ViewDirection.hlsl"
#include "Assets/Shader/Include/LightDirection.hlsl"
#include "Assets/Shader/Include/ShadowColor.hlsl"
#include "Assets/Shader/Include/PointLight.hlsl"
#include "Assets/Shader/Include/Ramp.hlsl"
#include "Assets/Shader/Include/RimLight.hlsl"


float4 _MainTex_ST;
float4 _MainColor;

float _DebugOutlineWidth;
float _OutlineWidth;
float4 _OutlineColor;


CBUFFER_END
#endif

