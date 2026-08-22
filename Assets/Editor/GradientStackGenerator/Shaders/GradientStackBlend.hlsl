// GradientStackGenerator - GradientStackBlend.hlsl
// 縦合成テクスチャの各帯(バンド)に埋め込まれた「合成モード番号」を Alpha から復号し、
// モードに応じた合成計算を行うためのインクルード。
//
// モード番号は Alpha に (mode + 0.5) / 6 でエンコードされている。
//   0 = Add(加算) / 1 = Multiply(乗算) / 2 = Overlay(オーバーレイ)
//   3,4,5 = 予約（あとで実装：現状は src をそのまま返す）
//
// テクスチャは sRGB OFF / Filter Point 推奨（Alphaのモード番号がにじまないように）。

#ifndef GRADIENT_STACK_BLEND_INCLUDED
#define GRADIENT_STACK_BLEND_INCLUDED

#define GS_BLEND_COUNT 6

// Alpha(0..1) から合成モード番号(0..5)を復号
int GS_DecodeBlendMode(float a)
{
    int m = (int)floor(saturate(a) * GS_BLEND_COUNT);
    return clamp(m, 0, GS_BLEND_COUNT - 1);
}

// base = 下地, src = このバンドの色
float3 GS_Blend_Add(float3 base, float3 src)      { return base + src; }
float3 GS_Blend_Multiply(float3 base, float3 src) { return base * src; }
float3 GS_Blend_Overlay(float3 base, float3 src)
{
    float3 low  = 2.0 * base * src;
    float3 high = 1.0 - 2.0 * (1.0 - base) * (1.0 - src);
    float3 sel  = step(0.5, base); // base>=0.5 → 1
    return lerp(low, high, sel);
}

// ▼▼ ここに残り3種を後から実装 ▼▼
float3 GS_Blend_Reserved4(float3 base, float3 src) { return src; } // TODO
float3 GS_Blend_Reserved5(float3 base, float3 src) { return src; } // TODO
float3 GS_Blend_Reserved6(float3 base, float3 src) { return src; } // TODO
// ▲▲ ここまで ▲▲

// モード番号でディスパッチ
float3 GS_ApplyBlend(int mode, float3 base, float3 src)
{
    if (mode == 0) return GS_Blend_Add(base, src);
    if (mode == 1) return GS_Blend_Multiply(base, src);
    if (mode == 2) return GS_Blend_Overlay(base, src);
    if (mode == 3) return GS_Blend_Reserved4(base, src);
    if (mode == 4) return GS_Blend_Reserved5(base, src);
    return GS_Blend_Reserved6(base, src);
}

// マスク付き適用: mask=1(白)で合成する / mask=0(黒)で下地そのまま（中間は部分適用）
float3 GS_ApplyBlendMasked(int mode, float3 base, float3 src, float mask)
{
    float m = saturate(mask);
    // 完全に黒なら合成計算をスキップして下地を返す
    if (m <= 0.0) return base;
    float3 blended = GS_ApplyBlend(mode, base, src);
    return lerp(base, blended, m);
}

// 便利関数: 縦積みテクスチャの全バンドを順に合成する
// tex/sampler: 縦積みテクスチャ, uvX: 横方向(0..1), bandCount: 帯の数, base: 初期の下地色
#if defined(SHADER_API_D3D11) || defined(UNITY_COMPILER_HLSL)
float3 GS_Composite(Texture2D tex, SamplerState smp, float uvX, int bandCount, float3 base)
{
    for (int i = 0; i < bandCount; i++)
    {
        float v = (i + 0.5) / bandCount;
        float4 c = tex.SampleLevel(smp, float2(uvX, v), 0);
        int mode = GS_DecodeBlendMode(c.a);
        base = GS_ApplyBlend(mode, base, c.rgb);
    }
    return base;
}

// マスク付き（mask: 白=処理する / 黒=処理しない）
float3 GS_CompositeMasked(Texture2D tex, SamplerState smp, float uvX, int bandCount, float3 base, float mask)
{
    for (int i = 0; i < bandCount; i++)
    {
        float v = (i + 0.5) / bandCount;
        float4 c = tex.SampleLevel(smp, float2(uvX, v), 0);
        int mode = GS_DecodeBlendMode(c.a);
        base = GS_ApplyBlendMasked(mode, base, c.rgb, mask);
    }
    return base;
}
#endif

// sampler2D 版（Built-in RP 向け）
float3 GS_CompositeTex2D(sampler2D tex, float uvX, int bandCount, float3 base)
{
    for (int i = 0; i < bandCount; i++)
    {
        float v = (i + 0.5) / bandCount;
        float4 c = tex2Dlod(tex, float4(uvX, v, 0, 0));
        int mode = GS_DecodeBlendMode(c.a);
        base = GS_ApplyBlend(mode, base, c.rgb);
    }
    return base;
}

// マスク付き（mask: 白=処理する / 黒=処理しない）
float3 GS_CompositeTex2DMasked(sampler2D tex, float uvX, int bandCount, float3 base, float mask)
{
    for (int i = 0; i < bandCount; i++)
    {
        float v = (i + 0.5) / bandCount;
        float4 c = tex2Dlod(tex, float4(uvX, v, 0, 0));
        int mode = GS_DecodeBlendMode(c.a);
        base = GS_ApplyBlendMasked(mode, base, c.rgb, mask);
    }
    return base;
}

#endif // GRADIENT_STACK_BLEND_INCLUDED
