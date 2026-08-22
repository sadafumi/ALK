#ifndef REMAP
#define REMAP

//float Remap(float In, float InMin, float InMax, float OutMin, float OutMax)
//{
//    float Out = OutMin + (In - InMin) * (OutMax - OutMin) / (InMax - InMin);
//    return Out;
//}
float2 Remap2(float2 InMin, float2 InMax, float2 OutMin, float2 OutMax, float2 In)
{
    return OutMin + (In - InMin) * (OutMax - OutMin) / (InMax - InMin);
}
#endif