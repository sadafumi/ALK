// MeshNormalBaker - MeshNormalBakerCore.cs
// メッシュの法線をUV空間へベイクして法線マップ(RenderTexture)を得る中核ロジック。
// 単一メッシュ・サブメッシュ選択に対応。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeshNormalBaker
{
    public enum NormalSpace { Object = 0, World = 1 }

    public static class MeshNormalBakerCore
    {
        const string kNormalBakeShader = "Hidden/MeshNormalBaker/NormalBake";
        const string kDilationShader = "Hidden/MeshNormalBaker/Dilation";

        static Material s_NormalMat;
        static Material s_DilationMat;

        static Material GetMaterial(ref Material cache, string shaderName)
        {
            if (cache != null) return cache;
            Shader sh = Shader.Find(shaderName);
            if (sh == null)
            {
                Debug.LogError($"[MeshNormalBaker] シェーダが見つかりません: {shaderName}\n" +
                               "Shaders フォルダがプロジェクトに含まれているか確認してください。");
                return null;
            }
            cache = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            return cache;
        }

        /// <summary>
        /// メッシュの法線を UV 空間へベイクし、エンコード済み法線 RenderTexture を返す。
        /// submeshes が null/空なら全サブメッシュ、指定があればそのサブメッシュのみを焼く。
        /// alpha=1 がマップ済み、alpha=0 が未マップ。
        /// </summary>
        public static RenderTexture BakeNormalMap(Mesh mesh, IList<int> submeshes, int resolution,
                                                  NormalSpace space, Matrix4x4 worldMatrix,
                                                  bool flipY, int dilationSteps)
        {
            if (mesh == null)
            {
                Debug.LogError("[MeshNormalBaker] メッシュが指定されていません。");
                return null;
            }
            if (mesh.uv == null || mesh.uv.Length == 0)
            {
                Debug.LogError($"[MeshNormalBaker] メッシュ '{mesh.name}' に UV(uv0) がありません。ベイクにはUV展開が必要です。");
                return null;
            }

            Material mat = GetMaterial(ref s_NormalMat, kNormalBakeShader);
            if (mat == null) return null;

            mat.SetFloat("_NormalSpace", (float)space);
            mat.SetFloat("_FlipY", flipY ? 1f : 0f);

            int subCount = Mathf.Max(1, mesh.subMeshCount);
            // 描画対象サブメッシュ
            var targets = new List<int>();
            if (submeshes == null || submeshes.Count == 0)
            {
                for (int s = 0; s < subCount; s++) targets.Add(s);
            }
            else
            {
                foreach (int s in submeshes)
                    if (s >= 0 && s < subCount && !targets.Contains(s)) targets.Add(s);
            }
            if (targets.Count == 0)
            {
                Debug.LogError("[MeshNormalBaker] ベイク対象のサブメッシュが選択されていません。");
                return null;
            }

            var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = false,
                msaaSamples = 1,
            };
            RenderTexture rt = RenderTexture.GetTemporary(desc);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0.5f, 0.5f, 0.5f, 0f));

            GL.PushMatrix();
            GL.LoadIdentity();

            Matrix4x4 m = (space == NormalSpace.World) ? worldMatrix : Matrix4x4.identity;
            if (mat.SetPass(0))
            {
                foreach (int s in targets)
                    Graphics.DrawMeshNow(mesh, m, s);
            }

            GL.PopMatrix();
            RenderTexture.active = prevActive;

            RenderTexture result;
            if (dilationSteps > 0)
            {
                result = Dilate(rt, dilationSteps);
            }
            else
            {
                result = new RenderTexture(desc) { name = "NormalBake" };
                result.filterMode = FilterMode.Bilinear;
                result.wrapMode = TextureWrapMode.Clamp;
                result.Create();
                Graphics.Blit(rt, result);
            }
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        public static RenderTexture Dilate(RenderTexture src, int steps)
        {
            Material mat = GetMaterial(ref s_DilationMat, kDilationShader);
            if (mat == null) return src;

            var desc = src.descriptor;
            RenderTexture a = RenderTexture.GetTemporary(desc);
            RenderTexture b = RenderTexture.GetTemporary(desc);
            a.filterMode = b.filterMode = FilterMode.Bilinear;
            a.wrapMode = b.wrapMode = TextureWrapMode.Clamp;

            Graphics.Blit(src, a);
            RenderTexture read = a, write = b;
            for (int i = 0; i < steps; i++)
            {
                Graphics.Blit(read, write, mat);
                RenderTexture tmp = read; read = write; write = tmp;
            }

            RenderTexture result = new RenderTexture(desc) { name = "NormalBake_Dilated" };
            result.filterMode = FilterMode.Bilinear;
            result.wrapMode = TextureWrapMode.Clamp;
            result.Create();
            Graphics.Blit(read, result);

            RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(b);
            return result;
        }

        /// <summary>RenderTexture の内容を PNG として保存する。プロジェクト内なら自動インポート。</summary>
        public static bool SaveRenderTextureToPng(RenderTexture rt, string absolutePath, bool linear)
        {
            if (rt == null || string.IsNullOrEmpty(absolutePath)) return false;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, linear);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            try
            {
                string dir = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(absolutePath, png);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MeshNormalBaker] PNG保存に失敗: {e.Message}");
                return false;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/"))
            {
                string relative = normalized.Substring(projectRoot.Length + 1);
                AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(relative) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = false; // 法線はデータ
                    importer.SaveAndReimport();
                }
            }
            return true;
        }
    }
}
