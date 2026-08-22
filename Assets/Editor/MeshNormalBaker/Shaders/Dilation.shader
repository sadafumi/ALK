// MeshNormalBaker - Dilation.shader
// UVアイランドの外周を1pxずつ埋める（シーム/縁のにじみ対策）。マップ済み A=1、未マップ A=0。
Shader "Hidden/MeshNormalBaker/Dilation"
{
    Properties { _MainTex ("Texture", 2D) = "black" {} }
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

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float4 frag (v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                if (c.a > 0.5) return c;

                float2 ts = _MainTex_TexelSize.xy;
                float4 sum = float4(0,0,0,0);
                float count = 0.0;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        float4 s = tex2D(_MainTex, i.uv + float2(x, y) * ts);
                        if (s.a > 0.5) { sum.rgb += s.rgb; count += 1.0; }
                    }
                }

                if (count > 0.0) return float4(sum.rgb / count, 1.0);
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
