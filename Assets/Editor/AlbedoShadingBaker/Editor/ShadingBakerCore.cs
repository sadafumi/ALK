// AlbedoShadingBaker - ShadingBakerCore.cs
// ベイク処理・ディレーション・陰影マスク生成・PNG保存の中核ロジック。
// EditorWindow(ShadingBakerWindow) から呼び出される。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlbedoShadingBaker
{
    public enum NormalSpace
    {
        Object = 0,
        World = 1,
    }

    public enum ShadingOutputMode
    {
        Mask = 0,       // グレースケール陰影マスク（中間0.5）
        AlbedoTimesShade = 1, // アルベド×陰影
    }

    /// <summary>
    /// メッシュ→法線マップ→陰影マスクのベイク処理を提供する静的ユーティリティ。
    /// RenderTexture はリソース保持のため呼び出し側で管理し、明示的に Release する。
    /// </summary>
    public static class ShadingBakerCore
    {
        const string kNormalBakeShader = "Hidden/AlbedoShadingBaker/NormalBake";
        const string kDilationShader   = "Hidden/AlbedoShadingBaker/Dilation";
        const string kShadingShader    = "Hidden/AlbedoShadingBaker/ShadingMask";

        static Material s_NormalMat;
        static Material s_DilationMat;
        static Material s_ShadingMat;

        static Material GetMaterial(ref Material cache, string shaderName)
        {
            if (cache != null) return cache;
            Shader sh = Shader.Find(shaderName);
            if (sh == null)
            {
                Debug.LogError($"[AlbedoShadingBaker] シェーダが見つかりません: {shaderName}\n" +
                               "Shaders フォルダがプロジェクトに含まれているか確認してください。");
                return null;
            }
            cache = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            return cache;
        }

        /// <summary>
        /// メッシュの法線を UV 空間へベイクし、エンコード済み法線 RenderTexture を返す。
        /// alpha=1 がマップ済み画素、alpha=0 が未マップ画素。
        /// </summary>
        /// <summary>単一メッシュ用の簡易オーバーロード。</summary>
        public static RenderTexture BakeNormalMap(Mesh mesh, int resolution, NormalSpace space,
                                                  Matrix4x4 worldMatrix, bool flipY, int dilationSteps)
        {
            return BakeNormalMap(new[] { mesh }, new[] { worldMatrix }, resolution, space, flipY, dilationSteps);
        }

        /// <summary>
        /// 複数メッシュを同じUV空間の1枚のRenderTextureへまとめて焼き込む。
        /// 単一テクスチャを複数メッシュが共有している場合に、単一の法線/陰影を得るために使う。
        /// meshes と matrices は同じ長さ・同じ順。matrices は World 空間指定時のみ使用。
        /// </summary>
        public static RenderTexture BakeNormalMap(IList<Mesh> meshes, IList<Matrix4x4> matrices,
                                                  int resolution, NormalSpace space, bool flipY, int dilationSteps)
        {
            if (meshes == null || meshes.Count == 0)
            {
                Debug.LogError("[AlbedoShadingBaker] メッシュが指定されていません。");
                return null;
            }

            // 有効なメッシュ(UVあり)を抽出
            var valid = new List<int>();
            for (int i = 0; i < meshes.Count; i++)
            {
                Mesh mesh = meshes[i];
                if (mesh == null) continue;
                if (mesh.uv == null || mesh.uv.Length == 0)
                {
                    Debug.LogWarning($"[AlbedoShadingBaker] メッシュ '{mesh.name}' に UV が無いためスキップします。");
                    continue;
                }
                valid.Add(i);
            }
            if (valid.Count == 0)
            {
                Debug.LogError("[AlbedoShadingBaker] UVを持つ有効なメッシュがありません。");
                return null;
            }

            Material mat = GetMaterial(ref s_NormalMat, kNormalBakeShader);
            if (mat == null) return null;

            mat.SetFloat("_NormalSpace", (float)space);
            mat.SetFloat("_FlipY", flipY ? 1f : 0f);

            var desc = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGB32, 0)
            {
                sRGB = false, // 法線はデータなのでリニア
                msaaSamples = 1,
            };
            RenderTexture rt = RenderTexture.GetTemporary(desc);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;

            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;

            // 未マップ=(0.5,0.5,0.5,0)。フラット法線相当かつ alpha=0。
            GL.Clear(true, true, new Color(0.5f, 0.5f, 0.5f, 0f));

            GL.PushMatrix();
            GL.LoadIdentity(); // 頂点シェーダがUVから直接クリップ座標を作るため行列は使わない

            if (mat.SetPass(0))
            {
                // 全メッシュを同じUV空間へ重ねて描画（クリアは最初の一度のみ）
                foreach (int i in valid)
                {
                    Mesh mesh = meshes[i];
                    Matrix4x4 m = Matrix4x4.identity;
                    if (space == NormalSpace.World && matrices != null && i < matrices.Count)
                        m = matrices[i];

                    int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
                    for (int s = 0; s < subMeshCount; s++)
                        Graphics.DrawMeshNow(mesh, m, s);
                }
            }

            GL.PopMatrix();
            RenderTexture.active = prevActive;

            RenderTexture normalResult;
            if (dilationSteps > 0)
            {
                normalResult = Dilate(rt, dilationSteps); // 永続RTを返す
            }
            else
            {
                // 呼び出し側が保持できるよう永続RTへコピー
                normalResult = new RenderTexture(desc) { name = "NormalBake" };
                normalResult.filterMode = FilterMode.Bilinear;
                normalResult.wrapMode = TextureWrapMode.Clamp;
                normalResult.Create();
                Graphics.Blit(rt, normalResult);
            }
            RenderTexture.ReleaseTemporary(rt);
            return normalResult;
        }

        /// <summary>UVアイランド外周をstep回だけ埋める。</summary>
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

            // 結果を新しい永続 RT にコピーして返す（呼び出し側管理）
            RenderTexture result = new RenderTexture(desc) { name = "NormalBake_Dilated" };
            result.filterMode = FilterMode.Bilinear;
            result.wrapMode = TextureWrapMode.Clamp;
            result.Create();
            Graphics.Blit(read, result);

            RenderTexture.ReleaseTemporary(a);
            RenderTexture.ReleaseTemporary(b);
            return result;
        }

        /// <summary>
        /// 法線 RenderTexture から陰影マスクを生成し、dst に書き込む。
        /// lightDir は法線と同じ空間（Object/World）の方向ベクトル。
        /// </summary>
        public static void GenerateShading(RenderTexture normalRT, RenderTexture dst,
                                           Vector3 lightDir, float contrast, float ambient,
                                           ShadingOutputMode mode, Texture albedo)
        {
            Material mat = GetMaterial(ref s_ShadingMat, kShadingShader);
            if (mat == null || normalRT == null || dst == null) return;

            if (lightDir.sqrMagnitude < 1e-6f) lightDir = Vector3.forward;
            lightDir.Normalize();

            mat.SetVector("_LightDir", new Vector4(lightDir.x, lightDir.y, lightDir.z, 0f));
            mat.SetFloat("_Contrast", Mathf.Max(0f, contrast));
            mat.SetFloat("_Ambient", Mathf.Clamp01(ambient));
            mat.SetFloat("_Mode", (float)mode);
            if (albedo != null) mat.SetTexture("_AlbedoTex", albedo);

            Graphics.Blit(normalRT, dst, mat);
        }

        /// <summary>
        /// キャビティ(AO寄り)の陰影を生成する。ライト方向は使わない。
        /// 各ピクセルの周囲を複数方向×複数半径で探索し、「周囲の壁がこちらを向いている度合い(オクルージョン)」を評価する。
        ///   影が出やすい(谷/凹＝周囲の壁に囲まれている) → 黒 / 影が出づらい(凸/開けている) → 白 / 平坦 → グレー(0.5)。
        /// 窪みの縁だけでなく領域全体が暗くなる。グレー基準(0.5)はテクスチャ全体の平均に対応する。
        /// </summary>
        public static Color[] GenerateCavity(RenderTexture normalRT, int radius, int directions,
                                             float gain, bool autoNormalize, bool invert,
                                             float minValidRatio,
                                             out int width, out int height)
        {
            minValidRatio = Mathf.Clamp01(minValidRatio);
            width = normalRT.width;
            height = normalRT.height;
            int len = width * height;

            // 法線RTをCPUへ読み出し
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = normalRT;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, /*linear*/ true);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Color[] enc = tex.GetPixels();
            Object.DestroyImmediate(tex);

            // 法線デコード & マップ判定
            var N = new Vector3[len];
            var mask = new bool[len];
            for (int i = 0; i < len; i++)
            {
                Color c = enc[i];
                Vector3 n = new Vector3(c.r * 2f - 1f, c.g * 2f - 1f, c.b * 2f - 1f);
                float mag = n.magnitude;
                N[i] = (mag > 1e-6f) ? n / mag : new Vector3(0, 0, 1);
                mask[i] = c.a >= 0.5f;
            }

            int D = Mathf.Max(4, directions);
            int r = Mathf.Max(1, radius);

            // 探索方向(円周上に D 個の単位UV方向)
            var dirX = new float[D];
            var dirY = new float[D];
            for (int k = 0; k < D; k++)
            {
                float ang = 2f * Mathf.PI * k / D;
                dirX[k] = Mathf.Cos(ang);
                dirY[k] = Mathf.Sin(ang);
            }

            // 半径方向のサンプル数(領域全体を埋めるため複数半径を探索)
            int RS = Mathf.Clamp(r, 2, 6);

            // 各ピクセルのオクルージョン量
            var raw = new float[len];
            var confident = new bool[len]; // 周囲のノーマル有効率が十分なピクセル
            double sum = 0.0;
            int cnt = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    if (!mask[idx]) { raw[idx] = 0f; continue; }

                    float acc = 0f;
                    int used = 0;
                    int valid = 0;
                    for (int k = 0; k < D; k++)
                    {
                        for (int j = 1; j <= RS; j++)
                        {
                            float rad = r * (float)j / RS;
                            int nx = Mathf.Clamp(x + Mathf.RoundToInt(dirX[k] * rad), 0, width - 1);
                            int ny = Mathf.Clamp(y + Mathf.RoundToInt(dirY[k] * rad), 0, height - 1);
                            int nidx = ny * width + nx;

                            // 探索方向は常に1本として数える（片側偏りによる縁の不正値を防ぐ）
                            used++;

                            // ノーマルの無い方向(メッシュ外)＝壁が無い＝開けている → 遮蔽0（寄与なし）
                            if (!mask[nidx]) continue;
                            valid++;

                            // 隣の壁の法線がこちら(-方向)を向いていれば occlusion 増
                            float facing = -(N[nidx].x * dirX[k] + N[nidx].y * dirY[k]);
                            if (facing > 0f) acc += facing; // 手前に覆いかぶさる壁のみ寄与
                        }
                    }
                    float v = (used > 0) ? acc / used : 0f;
                    raw[idx] = v;

                    // 周囲のノーマル有効率が低い＝信頼できないピクセルは統計から除外し、後でグレーにする
                    bool ok = used > 0 && (valid / (float)used) >= minValidRatio;
                    confident[idx] = ok;
                    if (ok) { sum += v; cnt++; }
                }
            }

            // 全体平均を中央(グレー)に。必要なら標準偏差で自動正規化
            float mean = (cnt > 0) ? (float)(sum / cnt) : 0f;
            float std = 1f;
            if (autoNormalize)
            {
                double vsum = 0.0;
                for (int i = 0; i < len; i++)
                {
                    if (!confident[i]) continue;
                    double d = raw[i] - mean;
                    vsum += d * d;
                }
                std = (cnt > 0) ? Mathf.Sqrt((float)(vsum / cnt)) : 1f;
                if (std < 1e-6f) std = 1f;
            }

            const float kSigma = 2.5f; // ±kσ をフルレンジ目安に
            // 既定(invert=false): オクルージョン大(谷) → 黒 / 小(凸) → 白
            float s = invert ? 1f : -1f;
            var outPix = new Color[len];
            for (int i = 0; i < len; i++)
            {
                // メッシュ無し or 信頼できない（周囲のノーマルが少ない）ピクセルはグレー(0.5)
                if (!confident[i]) { outPix[i] = new Color(0.5f, 0.5f, 0.5f, 1f); continue; }
                float d = raw[i] - mean;
                float norm = autoNormalize ? d / (kSigma * std) : d;
                float val = 0.5f + 0.5f * gain * s * norm;
                if (float.IsNaN(val) || float.IsInfinity(val)) val = 0.5f; // 念のため
                val = Mathf.Clamp01(val);
                outPix[i] = new Color(val, val, val, 1f);
            }
            return outPix;
        }

        /// <summary>ヨー/ピッチ(度)から方向ベクトルを作る。</summary>
        public static Vector3 DirectionFromYawPitch(float yawDeg, float pitchDeg)
        {
            float yaw = yawDeg * Mathf.Deg2Rad;
            float pitch = pitchDeg * Mathf.Deg2Rad;
            float cp = Mathf.Cos(pitch);
            return new Vector3(
                Mathf.Sin(yaw) * cp,
                Mathf.Sin(pitch),
                Mathf.Cos(yaw) * cp
            ).normalized;
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
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(absolutePath, png);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AlbedoShadingBaker] PNG保存に失敗: {e.Message}");
                return false;
            }

            // Assets 配下なら相対パスに変換してインポート
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/"))
            {
                string relative = normalized.Substring(projectRoot.Length + 1);
                AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
            }
            return true;
        }
    }
}
