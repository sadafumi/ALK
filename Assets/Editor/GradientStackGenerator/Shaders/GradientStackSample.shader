// GradientStackGenerator - GradientStackSample.shader
// 縦積みGradientテクスチャ(_StackTex)を使う実装サンプル（Built-in RP / Unlit）。
//   ・各帯(バンド)を「ドライバ値」でランプ参照し、Alphaに埋め込まれた合成モードで下地に重ねる
//   ・合成計算は同梱 GradientStackBlend.hlsl の関数で行う
//
// _StackTex は sRGB OFF / Filter Point でインポートしてください（本ツールの保存で自動設定されます）。
Shader "Sample/GradientStackSample"
{
    Properties
    {
        _BaseTex ("Base Texture", 2D) = "white" {}
        _BaseColor ("Base Color (下地)", Color) = (0,0,0,1)
        _StackTex ("Gradient Stack (縦積み)", 2D) = "black" {}
        [IntRange] _BandCount ("Band Count (帯の数)", Range(1,8)) = 6

        _MaskTex ("Mask (白=処理 / 黒=しない)", 2D) = "white" {}
        [Toggle] _MaskInvert ("Mask 反転", Float) = 0

        [Toggle] _UseLuminance ("Driver: 0=UV.x / 1=Base輝度", Float) = 1
        _Tiling ("UV.x タイリング (Driver=UV.x時)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "GradientStackBlend.hlsl" // 同フォルダのインクルード

            sampler2D _BaseTex;  float4 _BaseTex_ST;
            sampler2D _StackTex;
            sampler2D _MaskTex;  float4 _MaskTex_ST;
            float4 _BaseColor;
            float _BandCount;
            float _MaskInvert;
            float _UseLuminance;
            float _Tiling;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uvBase : TEXCOORD1; float2 uvMask : TEXCOORD2; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.uvBase = TRANSFORM_TEX(v.uv, _BaseTex);
                o.uvMask = TRANSFORM_TEX(v.uv, _MaskTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 baseTex = tex2D(_BaseTex, i.uvBase).rgb;

                // ランプを引く位置(0..1)を決めるドライバ値
                float driverUV  = frac(i.uv.x * _Tiling);
                float lum       = dot(baseTex, float3(0.2126, 0.7152, 0.0722));
                float driver    = lerp(driverUV, lum, saturate(_UseLuminance));

                // マスク: 白=処理する / 黒=処理しない
                float mask = tex2D(_MaskTex, i.uvMask).r;
                if (_MaskInvert > 0.5) mask = 1.0 - mask;

                // 下地色から開始し、マスクの効く所だけ全バンドを合成
                float3 baseCol = _BaseColor.rgb;
                int bandCount = (int)round(_BandCount);
                float3 col = GS_CompositeTex2DMasked(_StackTex, driver, bandCount, baseCol, mask);

                return float4(saturate(col), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
