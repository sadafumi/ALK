using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PCSS.Editor
{
    /// <summary>
    /// アクティブなURPアセットが参照するすべてのRendererにPCSSShadowsFeatureを
    /// 追加/削除するメニュー。手動で行う場合はRendererアセットのInspectorで
    /// "Add Renderer Feature" → "PCSS Shadows Feature" を選択する。
    /// </summary>
    public static class PCSSSetupMenu
    {
        private const string k_FeatureName = "PCSS Shadows";
        private const string k_ShaderName = "Hidden/PCSS/ScreenSpaceShadows";

        [MenuItem("Tools/PCSS/Add PCSS To All Renderers")]
        public static void AddToAllRenderers()
        {
            int added = 0;
            foreach (var rendererData in EnumerateRendererData())
            {
                if (HasFeature(rendererData, out _))
                {
                    Debug.Log($"[PCSS] '{rendererData.name}' には既にPCSSが追加されています。", rendererData);
                    continue;
                }

                WarnIfBuiltinScreenSpaceShadows(rendererData);

                if (AddFeature(rendererData))
                {
                    added++;
                    Debug.Log($"[PCSS] '{rendererData.name}' にPCSS Shadows Featureを追加しました。", rendererData);
                }
            }

            AssetDatabase.SaveAssets();
            if (added == 0)
                Debug.Log("[PCSS] 追加対象のRendererはありませんでした。");
        }

        [MenuItem("Tools/PCSS/Remove PCSS From All Renderers")]
        public static void RemoveFromAllRenderers()
        {
            foreach (var rendererData in EnumerateRendererData())
            {
                if (!HasFeature(rendererData, out var feature))
                    continue;

                var so = new SerializedObject(rendererData);
                var featuresProp = so.FindProperty("m_RendererFeatures");
                var mapProp = so.FindProperty("m_RendererFeatureMap");
                for (int i = featuresProp.arraySize - 1; i >= 0; i--)
                {
                    if (featuresProp.GetArrayElementAtIndex(i).objectReferenceValue == feature)
                    {
                        featuresProp.DeleteArrayElementAtIndex(i);
                        if (mapProp != null && i < mapProp.arraySize)
                            mapProp.DeleteArrayElementAtIndex(i);
                    }
                }
                so.ApplyModifiedProperties();
                Undo.DestroyObjectImmediate(feature);
                EditorUtility.SetDirty(rendererData);
                Debug.Log($"[PCSS] '{rendererData.name}' からPCSSを削除しました。", rendererData);
            }
            AssetDatabase.SaveAssets();
        }

        private static IEnumerable<ScriptableRendererData> EnumerateRendererData()
        {
            var seen = new HashSet<ScriptableRendererData>();
            var pipelines = new HashSet<UniversalRenderPipelineAsset>();

            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset defaultAsset)
                pipelines.Add(defaultAsset);
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset qualityAsset)
                    pipelines.Add(qualityAsset);
            }

            var fieldInfo = typeof(UniversalRenderPipelineAsset).GetField(
                "m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fieldInfo == null)
            {
                Debug.LogError("[PCSS] URP内部フィールド 'm_RendererDataList' が見つかりません。URPのバージョンが変わった可能性があります。");
                yield break;
            }

            foreach (var pipeline in pipelines)
            {
                if (fieldInfo.GetValue(pipeline) is not ScriptableRendererData[] list)
                    continue;
                foreach (var data in list)
                {
                    if (data != null && seen.Add(data))
                        yield return data;
                }
            }
        }

        private static bool HasFeature(ScriptableRendererData rendererData, out ScriptableRendererFeature found)
        {
            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature is PCSSShadowsFeature)
                {
                    found = feature;
                    return true;
                }
            }
            found = null;
            return false;
        }

        private static void WarnIfBuiltinScreenSpaceShadows(ScriptableRendererData rendererData)
        {
            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature != null && feature.GetType().Name == "ScreenSpaceShadows")
                {
                    Debug.LogWarning(
                        $"[PCSS] '{rendererData.name}' にURP内蔵のScreen Space Shadowsが追加されています。" +
                        "PCSSと競合するため、内蔵側を無効化または削除してください。", rendererData);
                }
            }
        }

        private static bool AddFeature(ScriptableRendererData rendererData)
        {
            var feature = ScriptableObject.CreateInstance<PCSSShadowsFeature>();
            feature.name = k_FeatureName;
            Undo.RegisterCreatedObjectUndo(feature, "Add PCSS Shadows Feature");

            // ビルドでShader.Findに頼らないよう、シェーダー参照をシリアライズしておく。
            var shader = Shader.Find(k_ShaderName);
            if (shader != null)
            {
                var featureSo = new SerializedObject(feature);
                featureSo.FindProperty("m_Shader").objectReferenceValue = shader;
                featureSo.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning($"[PCSS] シェーダー '{k_ShaderName}' が見つかりません。インポートが完了しているか確認してください。");
            }

            if (EditorUtility.IsPersistent(rendererData))
                AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var so = new SerializedObject(rendererData);
            var featuresProp = so.FindProperty("m_RendererFeatures");
            var mapProp = so.FindProperty("m_RendererFeatureMap");
            if (featuresProp == null)
            {
                Debug.LogError("[PCSS] 'm_RendererFeatures' が見つかりません。手動でRenderer Featureを追加してください。");
                Object.DestroyImmediate(feature, true);
                return false;
            }

            featuresProp.arraySize++;
            featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = feature;
            if (mapProp != null)
            {
                mapProp.arraySize++;
                mapProp.GetArrayElementAtIndex(mapProp.arraySize - 1).longValue = localId;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(rendererData);
            return true;
        }
    }
}
