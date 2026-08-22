// AlbedoShadingBaker - NormalBake.shader
// メッシュの法線をUV空間へ焼き込み、法線マップ（オブジェクト空間 / ワールド空間）を出力する。
// 頂点シェーダで UV を直接クリップ座標に変換するため、モデル/ビュー/プロジェクション行列は使用しない。
Shader "Hidden/AlbedoShadingBaker/NormalBake"
{
    Properties
    {
        // 0 = Object Space, 1 = World Space
        _NormalSpace ("Normal Space", Float) = 0
        // Y反転（プラットフォームや出力の上下が反転する場合に使用）
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

                // UV(0..1) を クリップ空間(-1..1) へ
                float2 clip = v.uv * 2.0 - 1.0;

                // RenderTexture の上下反転をプラットフォーム差含めて補正
                clip.y *= _ProjectionParams.x;
                if (_FlipY > 0.5) clip.y = -clip.y;

                o.pos = float4(clip, 0.0, 1.0);

                float3 n = normalize(v.normal);
                if (_NormalSpace > 0.5)
                {
                    // ワールド空間法線（メッシュを配置した向きを反映）
                    n = normalize(UnityObjectToWorldNormal(v.normal));
                }
                o.nrm = n;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.nrm);
                // -1..1 -> 0..1 にエンコード。A=1 で「UVにマップされている」ことを示す
                return float4(n * 0.5 + 0.5, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
