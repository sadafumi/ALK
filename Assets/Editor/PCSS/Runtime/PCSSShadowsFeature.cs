using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PCSS
{
    [Serializable]
    public class PCSSShadowsSettings
    {
        [Header("Light")]
        [Tooltip("光源の見かけの角直径(度)。大きいほど距離に応じて影が柔らかくなる。太陽の実際の値は約0.53°。")]
        [Range(0.1f, 10f)] public float lightAngularDiameter = 1.5f;

        [Header("Penumbra (world units)")]
        [Tooltip("最小ペナンブラ幅(m)。接地部分など遮蔽物との距離が0に近い場所の柔らかさ。")]
        [Min(0f)] public float minPenumbraWidth = 0.01f;
        [Tooltip("最大ペナンブラ幅(m)。フィルタ半径の暴走とカスケードタイル漏れを防ぐ上限。")]
        [Min(0.01f)] public float maxPenumbraWidth = 0.6f;

        [Header("Blocker Search")]
        [Tooltip("ブロッカー探索半径(m)。この距離までの遮蔽物がペナンブラ推定に寄与する。")]
        [Min(0.01f)] public float blockerSearchRadius = 0.4f;
        [Tooltip("ブロッカー判定の深度バイアス(シャドウマップ深度単位)。セルフシャドウのちらつきが出る場合に上げる。")]
        [Range(0f, 0.01f)] public float blockerDepthBias = 0.0005f;

        [Header("Quality")]
        [Tooltip("ブロッカー探索のサンプル数。")]
        [Range(4, 32)] public int blockerSampleCount = 16;
        [Tooltip("PCFフィルタのサンプル数。")]
        [Range(8, 64)] public int filterSampleCount = 32;
    }

    /// <summary>
    /// URP標準のシャドウフィルタリングをPCSS (Percentage-Closer Soft Shadows) に
    /// 置き換えるRenderer Feature。URP内蔵のScreen Space Shadowsと同じ仕組みで
    /// メインライトの影をスクリーンスペースで解決するため、シーン内のすべての
    /// Litシェーダーにそのまま適用される(シェーダー改変不要)。
    /// 内蔵の "Screen Space Shadows" Renderer Feature とは併用不可。
    /// </summary>
    [Tooltip("PCSS Screen Space Shadows")]
    public class PCSSShadowsFeature : ScriptableRendererFeature
    {
        [SerializeField, HideInInspector] private Shader m_Shader = null;
        [SerializeField] private PCSSShadowsSettings m_Settings = new PCSSShadowsSettings();

        private Material m_Material;
        private PCSSShadowsPass m_ShadowsPass = null;
        private PCSSShadowsPostPass m_ShadowsPostPass = null;

        private const string k_ShaderName = "Hidden/PCSS/ScreenSpaceShadows";

        public PCSSShadowsSettings settings => m_Settings;

        /// <inheritdoc/>
        public override void Create()
        {
            if (m_ShadowsPass == null)
                m_ShadowsPass = new PCSSShadowsPass();
            if (m_ShadowsPostPass == null)
                m_ShadowsPostPass = new PCSSShadowsPostPass();

            LoadMaterial();

            // After the depth prepass (and the depth-priming copy), before opaques/gbuffer.
            m_ShadowsPass.renderPassEvent = RenderPassEvent.AfterRenderingPrePasses + 1;
            m_ShadowsPostPass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        /// <inheritdoc/>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!LoadMaterial())
            {
                Debug.LogErrorFormat(
                    "{0}.AddRenderPasses(): Missing material. {1} render pass will not be added.",
                    GetType().Name, name);
                return;
            }

            bool allowMainLightShadows = renderingData.shadowData.supportsMainLightShadows
                                         && renderingData.lightData.mainLightIndex != -1;
            if (!allowMainLightShadows)
                return;

            m_ShadowsPass.Setup(m_Settings, m_Material, renderingData.shadowData.mainLightShadowCascadesCount);
            renderer.EnqueuePass(m_ShadowsPass);
            renderer.EnqueuePass(m_ShadowsPostPass);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            m_ShadowsPass = null;
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }

        private bool LoadMaterial()
        {
            if (m_Material != null)
                return true;

            if (m_Shader == null)
            {
                m_Shader = Shader.Find(k_ShaderName);
                if (m_Shader == null)
                    return false;
            }

            m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
            return m_Material != null;
        }

        internal static class ShaderConstants
        {
            public static readonly int _PCSSParams0 = Shader.PropertyToID("_PCSSParams0");
            public static readonly int _PCSSParams1 = Shader.PropertyToID("_PCSSParams1");
            public static readonly int _ScreenSpaceShadowmapTexture = Shader.PropertyToID("_ScreenSpaceShadowmapTexture");

            public static readonly GlobalKeyword MainLightShadows = GlobalKeyword.Create("_MAIN_LIGHT_SHADOWS");
            public static readonly GlobalKeyword MainLightShadowCascades = GlobalKeyword.Create("_MAIN_LIGHT_SHADOWS_CASCADE");
            public static readonly GlobalKeyword MainLightShadowScreen = GlobalKeyword.Create("_MAIN_LIGHT_SHADOWS_SCREEN");
        }

        private class PCSSShadowsPass : ScriptableRenderPass
        {
            private Material m_Material;

            internal PCSSShadowsPass()
            {
                profilingSampler = new ProfilingSampler("PCSS Screen Space Shadows");
            }

            internal void Setup(PCSSShadowsSettings settings, Material material, int cascadeCount)
            {
                m_Material = material;
                ConfigureInput(ScriptableRenderPassInput.Depth);

                float penumbraScale = 2.0f * Mathf.Tan(0.5f * settings.lightAngularDiameter * Mathf.Deg2Rad);
                material.SetVector(ShaderConstants._PCSSParams0, new Vector4(
                    penumbraScale,
                    settings.minPenumbraWidth,
                    Mathf.Max(settings.maxPenumbraWidth, settings.minPenumbraWidth),
                    settings.blockerSearchRadius));
                material.SetVector(ShaderConstants._PCSSParams1, new Vector4(
                    settings.blockerSampleCount,
                    settings.filterSampleCount,
                    settings.blockerDepthBias,
                    cascadeCount));
            }

            private class PassData
            {
                internal TextureHandle target;
                internal Material material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (m_Material == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                var desc = cameraData.cameraTargetDescriptor;
                desc.depthStencilFormat = GraphicsFormat.None;
                desc.msaaSamples = 1;
                desc.graphicsFormat = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, GraphicsFormatUsage.Blend)
                    ? GraphicsFormat.R8_UNorm
                    : GraphicsFormat.B8G8R8A8_UNorm;
                TextureHandle color = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "_ScreenSpaceShadowmapTexture", true);

                // UnsafePass: URP内蔵のScreenSpaceShadowsと同じ理由(UUM-85291、Deferredでの
                // パス結合による問題回避)でRasterPassではなくUnsafePassを使う。
                using (var builder = renderGraph.AddUnsafePass<PassData>(passName, out var passData, profilingSampler))
                {
                    passData.target = color;
                    passData.material = m_Material;
                    builder.UseTexture(color, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);

                    if (color.IsValid())
                        builder.SetGlobalTextureAfterPass(color, ShaderConstants._ScreenSpaceShadowmapTexture);

                    builder.SetRenderFunc((PassData data, UnsafeGraphContext rgContext) =>
                    {
                        ExecutePass(rgContext.cmd, data, data.target);
                    });
                }
            }

            private static void ExecutePass(UnsafeCommandBuffer cmd, PassData data, RTHandle target)
            {
                cmd.SetRenderTarget(target);
                Blitter.BlitTexture(cmd, target, Vector2.one, data.material, 0);
                cmd.SetKeyword(ShaderConstants.MainLightShadows, false);
                cmd.SetKeyword(ShaderConstants.MainLightShadowCascades, false);
                cmd.SetKeyword(ShaderConstants.MainLightShadowScreen, true);
            }
        }

        private class PCSSShadowsPostPass : ScriptableRenderPass
        {
            internal PCSSShadowsPostPass()
            {
                profilingSampler = new ProfilingSampler("PCSS Shadow Keywords Reset");
            }

            internal class PassData
            {
                internal UniversalShadowData shadowData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
                {
                    UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    passData.shadowData = frameData.Get<UniversalShadowData>();
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) =>
                    {
                        ExecutePass(rgContext.cmd, data.shadowData);
                    });
                }
            }

            // 透明オブジェクトはスクリーンスペースシャドウテクスチャを参照できない
            // (不透明の深度でしか解決していない)ため、通常のシャドウマップ
            // サンプリングに戻す。
            private static void ExecutePass(RasterCommandBuffer cmd, UniversalShadowData shadowData)
            {
                int cascadesCount = shadowData.mainLightShadowCascadesCount;
                bool mainLightShadows = shadowData.supportsMainLightShadows;

                cmd.SetKeyword(ShaderConstants.MainLightShadowScreen, false);
                cmd.SetKeyword(ShaderConstants.MainLightShadows, mainLightShadows && cascadesCount == 1);
                cmd.SetKeyword(ShaderConstants.MainLightShadowCascades, mainLightShadows && cascadesCount > 1);
            }
        }
    }
}
