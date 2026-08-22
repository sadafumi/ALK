// AlbedoShadingBaker - Dilation.shader
// UVアイランドの外周を1pxずつ埋める（シーム/縁のにじみ対策）。
// マップ済み画素は A=1、未マップは A=0。未マップ画素を周囲8方向のマップ済み画素で埋める。
Shader "Hidden/AlbedoShadingBaker/Dilation"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
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

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // (1/w, 1/h, w, h)

            float4 frag (v2f_img i) : SV_Target
            {
                float4 c = tex2D(_MainTex, i.uv);
                if (c.a > 0.5)
                    return c; // 既にマップ済みならそのまま

                // 周囲8近傍から最初に見つかったマップ済み画素で埋める
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
                        float2 offset = float2(x, y) * ts;
                        float4 s = tex2D(_MainTex, i.uv + offset);
                        if (s.a > 0.5)
                        {
                            sum.rgb += s.rgb;
                            count += 1.0;
                        }
                    }
                }

                if (count > 0.0)
                    return float4(sum.rgb / count, 1.0);

                return c; // 周囲にもマップ済みが無ければ据え置き
            }
            ENDCG
        }
    }
    Fallback Off
}
