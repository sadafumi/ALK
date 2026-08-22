#ifndef ALPHATEST
#define ALPHATEST
float _Cutoff;

void AlphaTest(float2 uv)
{
#if defined(_ALPHATEST_ON)
    Alpha(SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_MainTex, sampler_MainTex)).a, _MainColor, _Cutoff);
#endif
}
void AlphaTest(float sub_alpha)
{
#if defined(_ALPHATEST_ON)
    Alpha(sub_alpha, _MainColor, _Cutoff);
#endif
}
void AlphaTest(float2 uv, float sub_alpha)
{
#if defined(_ALPHATEST_ON)
    Alpha(SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_MainTex, sampler_MainTex)).a * sub_alpha, _MainColor, _Cutoff);
#endif
}
void AlphaTest(float4 color)
{
#if defined(_ALPHATEST_ON)
    Alpha(color.a, _MainColor, _Cutoff);
#endif
}
#endif