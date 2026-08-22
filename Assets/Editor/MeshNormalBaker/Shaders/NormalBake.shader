// MeshNormalBaker - NormalBake.shader
// メッシュの法線をUV空間へ焼き込み、法線マップ（オブジェクト空間 / ワールド空間）を出力する。
// 頂点シェーダで UV を直接クリップ座標に変換するため、モデル/ビュー/プロジェクション行列は使用しない。
Shader "Hidden/MeshNormalBaker/NormalBake"
{
    Properties
    {
        _NormalSpace ("Normal Space", Float) = 0 // 0=Object, 1=World
        _FlipY ("Flip Y", Float) = 0
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
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _NormalSpace;
            float _FlipY;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 nrm : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float2 clip = v.uv * 2.0 - 1.0;
                clip.y *= _ProjectionParams.x;
                if (_FlipY > 0.5) clip.y = -clip.y;
                o.pos = float4(clip, 0.0, 1.0);

                float3 n = normalize(v.normal);
                if (_NormalSpace > 0.5)
                    n = normalize(UnityObjectToWorldNormal(v.normal));
                o.nrm = n;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.nrm);
                return float4(n * 0.5 + 0.5, 1.0); // A=1 でマップ済みを示す
            }
            ENDCG
        }
    }
    Fallback Off
}
