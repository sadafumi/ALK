Shader "ALK/Character"
{
    Properties
    {
        [MainTexture] _MainTex("Main Map", 2D) = "white" {}
        [MainColor] _MainColor("Main Color", Color) = (1, 1, 1, 1)
     
        _ShadowMap("ShadowMap", 2D) = "white" {}
        _ShadowRampMap("ShadowRampMap", 2D) = "white" {}

        _ShadingAreaCorrection("Shading Area Correction",  Range(-0.5,0.5)) = 0

        _Packing1Tex("Packing1Tex", 2D) = "Black" {}
        _Packing2Tex("Packing2Tex", 2D) = "Black" {}
        _RampTex("RampTex", 2D) = "black" {}

        _RampMaskTex("RampMaskTex", 2D) = "white" {}
        
        _RimLightMaskTex("RimLightMaskTex", 2D) = "white" {}
        [HDR] _RimLightColor("RimLight Color", Color) = (1,1,1,1)
        _RimThreshold("Rim Threshold", Range(0,1)) = 0.4
		_RimSmooth("Rim Smooth", Range(0.001,1)) = 0.4
		_RimScale("Rim Scale", Range(-0.5,0.5)) = 0.4

       _EdgeEnd       ("Edge End (èIí[Çà≥èk / 0.9 êÑèß)", Range(0.5,1)) = 0.9
 
        _Cutoff("AlphaCutout", Range(0.0, 1.0)) = 0.5
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0

        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        _QueueOffset("Queue offset", Float) = 0.0

        [HideInInspector] _StencilNo("__StencilNo", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "URP ForwardLit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]

            Stencil {  
                Ref [_StencilNo]
                Comp always  
                Pass replace  
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _FORWARD_PLUS

            #define _MAIN_LIGHT_SHADOWS_CASCADE
            #define _ADDITIONAL_LIGHT_SHADOWS
            #define _ADDITIONAL_LIGHTS
            #pragma shader_feature _ENABLE_CUSTOM_CHARACTER_SHADOWMAP
            
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            #include "Assets/Shader/Include/input.hlsl"
            #include "Assets/Shader/Include/vert.hlsl"
            #include "Assets/Shader/Include/frag.hlsl"

            ENDHLSL
        }

        // Pass
        // {
        //     Name "Outline"
        //     Tags{"LightMode" = "UniversalForwardOnly"}

        //     Cull front
        //     //Cull[_SRPDefaultUnlitColMode]
        //     //ColorMask[_SPRDefaultUnlitColorMask]
        //     Stencil
        //     {
        //         Ref[_StencilNo]
        //         Comp[_StencilComp_Outline]
        //         Pass[_StencilOpPass_Outline]
        //         Fail[_StencilOpFail]
        //     }

        //     HLSLPROGRAM
        //     #pragma vertex vert
        //     #pragma fragment frag

        //     #define _MAIN_LIGHT_SHADOWS_CASCADE
        //     #define _ADDITIONAL_LIGHT_SHADOWS
        //     #define _ADDITIONAL_LIGHTS
        //     #pragma multi_compile _ _SHADOWS_SOFTD

        //     #pragma shader_feature_local_fragment _ALPHATEST_ON


        //     #pragma shader_feature _USE_OUTLINETEX
        //     #pragma shader_feature _USE_CLOUDSHADOWMASK
        //     #pragma shader_feature _USE_DECAL

        //     #include "Assets/Shader/Include/input.hlsl"
        //     #include "Assets/Shader/Include/Outline.hlsl"
   
        //     ENDHLSL
        // }
        Pass
        {
            Name "ShadowCaster"
            Tags{"LightMode" = "ShadowCaster"}

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM

            //#pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            

            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Assets/Shader/Include/input.hlsl"
            #include "Assets/Shader/Include/ShadowCaster.hlsl"
            
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags{"LightMode" = "DepthNormals"}

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM

            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            // -------------------------------------
            // Unity defined keywords
            
            // Universal Pipeline keywords
            #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            #include "Assets/Shader/Include/input.hlsl"
            #include "Assets/Shader/Include/DepthNormals.hlsl"

            ENDHLSL
        }
    }
    CustomEditor"CharacterShaderGUI"    
}
