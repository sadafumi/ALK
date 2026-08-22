struct InOutlineVert
{
    float4 vertex : POSITION;
    float4 normal : NORMAL;
    float4 tangent : TANGENT;
    float4 color : COLOR;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
};
struct InOutlineFrag
{
    float4 vertex : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 pos_w : TEXCOORD2;
    float4 local : TEXCOORD15;
    float4 color : TEXCOORD16;
};
float3 ViewDirectionOS(float3 positionOS)
{
    return TransformWorldToObject(_WorldSpaceCameraPos).xyz - positionOS;
}
float3 ViewDirectionVertex(float3 pos_w)
{
    return _WorldSpaceCameraPos.xyz - pos_w;
}

float GetOutlineWidth(float3 positionOS, float3 pos_w, float2 uv, float4 color, float outlineWidth, uint outlineVertexR2Width, float outlineFixWidth)
{
    outlineWidth *= 0.01;
    outlineWidth *= lerp(1.0, saturate(length(ViewDirection(pos_w))), outlineFixWidth);
    return outlineWidth;
}
float3 ToAbsolutepos_w(float3 positionRWS)
{
    return positionRWS + _WorldSpaceCameraPos.xyz;

}
float4 OptMul(float4x4 mat, float3 pos)
{
    return mat._m00_m10_m20_m30 * pos.x + (mat._m01_m11_m21_m31 * pos.y + (mat._m02_m12_m22_m32 * pos.z + mat._m03_m13_m23_m33));
}
float3 CalcOutlinePosition(inout float3 positionOS, float2 uv, float4 color, float3 normalOS, float3x3 tbnOS, float outlineWidth, uint outlineVertexR2Width, float outlineFixWidth, float outlineZBias)
{
    float3 pos_w = ToAbsolutepos_w(OptMul(GetObjectToWorldMatrix(), positionOS).xyz);
    float width = GetOutlineWidth(positionOS, pos_w, uv, color, outlineWidth, outlineVertexR2Width, outlineFixWidth);
    float3 outlineN = normalOS;

    if (outlineVertexR2Width == 2)
        outlineN = mul(color.rgb * 2.0 - 1.0, tbnOS);
    positionOS += outlineN * width;
    float3 V = ViewDirectionOS(positionOS);
    positionOS -= normalize(V) * outlineZBias;
    return width;
}

//float InterpolateY(float x)
//{
//    // 点 (0.5, 1.4) と (7, 0.78)
//    float x1 = 0.5;
//    float y1 = 1.4;
//    float x2 = 7.0;
//    float y2 = 0.68;

//    // 線形補間の式
//    float t = (x - x1) / (x2 - x1); // 正規化された位置
//    return lerp(y1, y2, t); // HLSL組み込みのlerp関数を使用
//}
float InterpolateY(float x)
{
    float x1 = 0.065;
    float y1 = 1.4;
    float x2 = 0.6;
    float y2 = 0.78;

    float t = (x - x1) / (x2 - x1); // 正規化された位置
    float result = lerp(y1, y2, t); // 線形補間結果

    return max(result, 0.5);
}
InOutlineFrag vert(InOutlineVert v)
{
    InOutlineFrag o;
    float4 vertexPos = v.vertex;
    float3 normal = v.normal.xyz;
    float3 tangent = v.tangent.xyz;
    normal = v.tangent.xyz;

    o.pos_w = mul(unity_ObjectToWorld, vertexPos).xyz;
    
    float3 bitangentOS = normalize(cross(normal, v.tangent.xyz)) * (v.tangent.w * length(normal));
    float3x3 tbnOS = float3x3(v.tangent.xyz, bitangentOS, v.normal.xyz);
    float outline_base_width = 0.1;
    float outline_width_weight = v.color.a;
    //float outline_width_weight = 0.5;
    float final_width = (_OutlineWidth) / unity_CameraProjection._m11;
    
    float fov = tan(1 / unity_CameraProjection._m11 / 2);
    float cameraLen = length(ViewDirectionVertex(o.pos_w.xyz)) * 1.1;
    
    //float a = DrawNumberAtLocalPos(vertexPos, float3(0, 0, 0), fov * cameraLen);

    float distanceScale = (fov * cameraLen);
    distanceScale *= InterpolateY(distanceScale);
    distanceScale = fov > 1 || fov < 0 ? 1 : distanceScale;
        
    float3 vec = CalcOutlinePosition(vertexPos.xyz, v.uv, v.color, normal, tbnOS, distanceScale * outline_width_weight * (_OutlineWidth), 0, 1, 1 - v.color.b);
    o.vertex = TransformObjectToHClip(vertexPos.xyz);
    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
    o.normalWS = TransformObjectToWorldNormal(normal).xyz;
    o.local = v.vertex;
    o.color = v.color;
    return o;
}

float4 frag(InOutlineFrag i) : SV_Target
{
    //float a = DrawNumberAtLocalPos(i.vertex, float3(0, 0, 0), i.color.b);
    //return float4(a, a, a,1);
    float2 uv = i.uv;
    float3 pos_w = i.pos_w;
    
    AlphaTest(uv);
    float4 pack1 = SAMPLE_TEXTURE2D(_Packing1Tex, sampler_Packing1Tex, uv);
#if defined(_USE_OUTLINETEX)
    float4 color = SAMPLE_TEXTURE2D(_OutlineColorTex, sampler_OutlineColorTex, uv);
#else
    float4 shadowColor = SAMPLE_TEXTURE2D(_ShadowMap, sampler_ShadowMap, uv);
    //float3 grayScaleColor = dot(shadowColor.rgb, float3(0.29, 0.58, 0.114));
    //float4 color = float4(lerp(shadowColor.rgb, shadowColor.rgb * shadowColor.rgb, grayScaleColor), 1);
    float4 color = float4(shadowColor.rgb * shadowColor.rgb, 1);
#endif
    float4 main_color = color;

    color.rgb *= _OutlineColor.rgb * _OutlineColor.a;
    
    i.normalWS = normalize(i.normalWS);

    float3 viewDirection = normalize(_WorldSpaceCameraPos.xyz - i.pos_w.xyz);

    float3 dir_color = 0;
    float3 lightDirection = LightDirection(i.pos_w, dir_color);
   
    float3 totalLight = 1;
    totalLight = PointLight(totalLight, pos_w, i.vertex, i.normalWS);
    color.rgb *= totalLight;

    
    return color;
}