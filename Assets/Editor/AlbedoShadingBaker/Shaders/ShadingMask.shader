// AlbedoShadingBaker - ShadingMask.shader
// 法線マップ(_MainTex, エンコード済み) とライト方向から Lambert 陰影マスクを生成する。
// N·L を 0.5 中心にリマップ（正面=1.0 / 直交=0.5 / 裏=0.0）してグレースケール出力。
Shader "Hidden/AlbedoShadingBaker/ShadingMask"
{
    Properties
    {
        _MainTex ("Normal (encoded)", 2D) = "bump" {}
        _LightDir ("Light Dir", Vector) = (0.3, 0.5, 0.8, 0)
        _Contrast ("Contrast", Float) = 1.0
        _Ambient ("Ambient", Float) = 0.0
        // 0 = グレースケール陰影マスク, 1 = アルベド×陰影
        _Mode ("Mode", Float) = 0
    }
    SubShader
    {
        Cull Off
        ZTest Always
        ZWrite Off
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;   // エンコード済み法線
            sampler2D _AlbedoTex; // 任意: アルベド×陰影モード用
            float4    _LightDir;
            float     _Contrast;
            float     _Ambient;
            float     _Mode;

            float4 frag (v2f_img i) : SV_Target
            {
                float4 enc = tex2D(_MainTex, i.uv);

                // 未マップ画素は中間(0.5)で塗る
                if (enc.a < 0.5)
                    return float4(0.5, 0.5, 0.5, 1.0);

                float3 n = normalize(enc.rgb * 2.0 - 1.0);
                float3 l = normalize(_LightDir.xyz);

                // Half-Lambert 風: N·L を 0.5 中心へ
                float ndl = dot(n, l);
                float shade = ndl * 0.5 + 0.5;

                // コントラスト（0.5中心を保つ）とアンビエント下限
                shade = saturate((shade - 0.5) * _Contrast + 0.5);
                shade = lerp(shade, 1.0, saturate(_Ambient));

                if (_Mode > 0.5)
                {
                    float3 albedo = tex2D(_AlbedoTex, i.uv).rgb;
                    return float4(albedo * shade, 1.0);
                }

                return float4(shade, shade, shade, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
