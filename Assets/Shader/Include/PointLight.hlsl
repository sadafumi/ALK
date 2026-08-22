#ifndef POINTLIGHT
#define POINTLIGHT

// ポイントライト全体制御パラメータ
float _PointLightGlobalIntensity;
float _PointLightGlobalThread;
float _PointLightGlobalSmoothness;
float _PointLightGlobalChrIntensity;

// -------------------------------------------------------
// CustomLight: URP Light の拡張ラッパー
//   pos, range を追加してシェーダー内の距離計算に使用
// -------------------------------------------------------
struct CustomLight
{
    float3 direction;
    float4 color;
    float distanceAttenuation;
    half shadowAttenuation;
    int type;
    float3 pos;
    float range;
};

// メインライト取得
CustomLight GetUrpMainUtsLight()
{
    CustomLight light;
    light.direction = _MainLightPosition.xyz;
    // unity_LightData.z: カリングマスクで除外されていなければ 1
    light.distanceAttenuation = unity_LightData.z;
#if defined(LIGHTMAP_ON) || defined(_MIXED_LIGHTING_SUBTRACTIVE)
    // Mixed Light のライトプローブオクルージョン
    light.distanceAttenuation *= unity_ProbesOcclusion.x;
#endif
    light.shadowAttenuation = 1.0;
    light.color = _MainLightColor;
    light.type = _MainLightPosition.w;
    light.pos = 0;
    light.range = 0;
    return light;
}

// 追加ライト取得 (perObjectLightIndex 直指定)
CustomLight GetAdditionalPerObjectUtsLight(int perObjectLightIndex, float3 positionWS, float4 positionCS)
{
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 lightPositionWS            = _AdditionalLightsBuffer[perObjectLightIndex].position;
    half4  color                      = _AdditionalLightsBuffer[perObjectLightIndex].color;
    half4  distanceAndSpotAttenuation = _AdditionalLightsBuffer[perObjectLightIndex].attenuation;
    half4  spotDirection              = _AdditionalLightsBuffer[perObjectLightIndex].spotDirection;
    half4  lightOcclusionProbeInfo    = _AdditionalLightsBuffer[perObjectLightIndex].occlusionProbeChannels;
#else
    float4 lightPositionWS = _AdditionalLightsPosition[perObjectLightIndex];
    half4 color = _AdditionalLightsColor[perObjectLightIndex];
    half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[perObjectLightIndex];
    half4 spotDirection = _AdditionalLightsSpotDir[perObjectLightIndex];
    half4 lightOcclusionProbeInfo = _AdditionalLightsOcclusionProbes[perObjectLightIndex];
#endif

    // w=0: 指向性ライト、w=1: ポイント/スポットライト
    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);
    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
    half attenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy)
                          * AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);

    CustomLight light;
    light.pos = lightPositionWS.xyz;
    light.direction = lightDirection;
    light.distanceAttenuation = attenuation;
    light.range = spotDirection.w;
    // この関数は頂点シェーダー (DetermineUTS_MainLightIndex) からも呼ばれるため
    // shadowAttenuation = 1.0 に固定する。
    // 影が必要なフラグメントシェーダーでは PointLight() 内の GetAdditionalLight() が担う。
    light.shadowAttenuation = 1.0;
    light.color = color;
    light.type = lightPositionWS.w;

#if defined(LIGHTMAP_ON) || defined(_MIXED_LIGHTING_SUBTRACTIVE)
    int  probeChannel           = lightOcclusionProbeInfo.x;
    half lightProbeContribution = lightOcclusionProbeInfo.y;
    half probeOcclusionValue    = unity_ProbesOcclusion[probeChannel];
    light.distanceAttenuation  *= max(probeOcclusionValue, lightProbeContribution);
#endif

    return light;
}

// 追加ライト取得 (ループインデックス → perObjectIndex 変換込み)
CustomLight GetAdditionalUtsLight(uint i, float3 positionWS, float4 positionCS)
{
    int perObjectLightIndex = GetPerObjectLightIndex(i);
    return GetAdditionalPerObjectUtsLight(perObjectLightIndex, positionWS, positionCS);
}

// -------------------------------------------------------
// PointLight: 追加ライットの処理
//
//   Forward+ はタイル/クラスター単位でライトを管理するため、
//   LIGHT_LOOP_BEGIN が必要。ただしマクロ内部で inputData.normalizedScreenSpaceUV
//   と inputData.positionWS を参照するため、関数内でローカルに構築して渡す。
//
//   Forward / Deferred は通常の for ループで対応。
//
//   shadowMask (unity_ProbesOcclusion) を GetAdditionalLight に
//   渡すことで Mixed Lighting のベイク済み影を正しく合成する。
//
//   ハーフランバート (NdotL * 0.5 + 0.5) でトゥーン調の
//   なだらかな陰影遷移を表現。
// -------------------------------------------------------
float3 PointLight(float3 totalLight, float3 positionWS, float4 positionCS, float3 normalWS)
{
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

#if defined(_FORWARD_PLUS)
    // LIGHT_LOOP_BEGIN が参照する inputData をローカルで構築する
    // positionCS は SV_POSITION のためフラグメント段階でスクリーン座標になっている
    InputData inputData = (InputData)0;
    inputData.positionWS             = positionWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS, unity_ProbesOcclusion);

        half halfLambert = dot(light.direction, normalWS) * 0.5 + 0.5;
        half3 attenuatedLightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
        totalLight += attenuatedLightColor * halfLambert * light.shadowAttenuation;
    LIGHT_LOOP_END

#else
    for (uint lightIndex = 0u; lightIndex < pixelLightCount; ++lightIndex)
    {
        Light light = GetAdditionalLight(lightIndex, positionWS, unity_ProbesOcclusion);

        half halfLambert = dot(light.direction, normalWS) * 0.5 + 0.5;
        half3 attenuatedLightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
        totalLight += attenuatedLightColor * halfLambert * light.shadowAttenuation;
    }
#endif // _FORWARD_PLUS

#endif // _ADDITIONAL_LIGHTS
    return totalLight;
}

#endif