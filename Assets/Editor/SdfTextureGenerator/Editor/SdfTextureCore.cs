// SdfTextureGenerator - SdfTextureCore.cs
// 複数枚の白黒グラデーション（顔影などの各段階）を1枚の段階的SDFテクスチャへ合成する中核ロジック。
// 参考: https://note.com/rid_316/n/ne967527e86ff
//   - 8bitだと段差/ジャギーが出るため、RG2chで16bit相当(65,536段階)にして滑らかにする
//   - 距離変換によるスムージングにも対応
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SdfTextureGenerator
{
    public enum SourceChannel { R, G, B, A, Luminance }

    public enum CombineMode
    {
        Average,        // 各段階(グラデーション)の平均。段階が既にグラデーションならこれが基本
        ThresholdStack, // しきい値以上の段階数/N。2値マスク列から段階SDFを作る
        DistanceBlend,  // 各段階を距離変換して符号付き距離でブレンド。少ない枚数でも滑らか
    }

    public enum OutputPrecision
    {
        Gray8,       // 8bit グレースケール PNG
        PackedRG16,  // 16bit相当を R(上位)+G(下位) にパック PNG（Unity DecodeFloatRG 互換）
        ExrFloat,    // EXR Float（真の高精度）
    }

    public static class SdfTextureCore
    {
        // ---------------- テクスチャ読み込み ----------------
        /// <summary>読み取り不可テクスチャでもBlit経由でストア値(0..1)として取得する。</summary>
        public static Color[] ReadTexture(Texture src, out int w, out int h)
        {
            w = src.width;
            h = src.height;
            var tmp = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, tmp);
            RenderTexture.active = tmp;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);
            Color[] px = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return px;
        }

        static Color SampleBilinear(Color[] px, int w, int h, float u, float v)
        {
            float fx = Mathf.Clamp01(u) * w - 0.5f;
            float fy = Mathf.Clamp01(v) * h - 0.5f;
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0;
            float ty = fy - y0;
            int x1 = Mathf.Clamp(x0 + 1, 0, w - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, h - 1);
            x0 = Mathf.Clamp(x0, 0, w - 1);
            y0 = Mathf.Clamp(y0, 0, h - 1);
            Color a = Color.Lerp(px[y0 * w + x0], px[y0 * w + x1], tx);
            Color b = Color.Lerp(px[y1 * w + x0], px[y1 * w + x1], tx);
            return Color.Lerp(a, b, ty);
        }

        static float PickChannel(Color c, SourceChannel ch)
        {
            switch (ch)
            {
                case SourceChannel.R: return c.r;
                case SourceChannel.G: return c.g;
                case SourceChannel.B: return c.b;
                case SourceChannel.A: return c.a;
                case SourceChannel.Luminance: return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            }
            return 0f;
        }

        /// <summary>ネイティブバッファを対象サイズ(dw×dh)のグレースケール float[] へリサンプル。</summary>
        public static float[] ResampleGray(Color[] src, int sw, int sh, int dw, int dh,
                                           SourceChannel ch, bool invert)
        {
            var dst = new float[dw * dh];
            for (int y = 0; y < dh; y++)
            {
                float v = (y + 0.5f) / dh;
                for (int x = 0; x < dw; x++)
                {
                    float u = (x + 0.5f) / dw;
                    float g = PickChannel(SampleBilinear(src, sw, sh, u, v), ch);
                    if (invert) g = 1f - g;
                    dst[y * dw + x] = Mathf.Clamp01(g);
                }
            }
            return dst;
        }

        // ---------------- 距離変換（チャンファー近似, member集合への距離[px]） ----------------
        static float[] DistanceTo(bool[] member, int w, int h)
        {
            const float BIG = 1e9f;
            const float a = 1f;
            const float b = 1.4142136f;
            var d = new float[w * h];
            for (int i = 0; i < d.Length; i++) d[i] = member[i] ? 0f : BIG;

            // 前方パス
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    float val = d[idx];
                    if (x > 0) val = Mathf.Min(val, d[idx - 1] + a);
                    if (y > 0) val = Mathf.Min(val, d[idx - w] + a);
                    if (x > 0 && y > 0) val = Mathf.Min(val, d[idx - w - 1] + b);
                    if (x < w - 1 && y > 0) val = Mathf.Min(val, d[idx - w + 1] + b);
                    d[idx] = val;
                }
            }
            // 後方パス
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = w - 1; x >= 0; x--)
                {
                    int idx = y * w + x;
                    float val = d[idx];
                    if (x < w - 1) val = Mathf.Min(val, d[idx + 1] + a);
                    if (y < h - 1) val = Mathf.Min(val, d[idx + w] + a);
                    if (x < w - 1 && y < h - 1) val = Mathf.Min(val, d[idx + w + 1] + b);
                    if (x > 0 && y < h - 1) val = Mathf.Min(val, d[idx + w - 1] + b);
                    d[idx] = val;
                }
            }
            return d;
        }

        /// <summary>内側(value>=threshold)を正とする符号付き距離[px]。</summary>
        static float[] SignedDistance(float[] gray, int w, int h, float threshold)
        {
            int len = w * h;
            var inside = new bool[len];
            var outside = new bool[len];
            for (int i = 0; i < len; i++)
            {
                bool ins = gray[i] >= threshold;
                inside[i] = ins;
                outside[i] = !ins;
            }
            var dIn = DistanceTo(inside, w, h);   // 内側までの距離(内側=0)
            var dOut = DistanceTo(outside, w, h); // 外側までの距離(外側=0)
            var sd = new float[len];
            for (int i = 0; i < len; i++) sd[i] = dOut[i] - dIn[i]; // 内側で正
            return sd;
        }

        // ---------------- 合成 ----------------
        /// <summary>複数段階のグレースケールを1枚のSDF値(0..1)へ合成する。</summary>
        public static float[] Combine(List<float[]> stages, int w, int h,
                                      CombineMode mode, float threshold, float maxDistPx)
        {
            int len = w * h;
            int n = stages.Count;
            var V = new float[len];
            if (n == 0) return V;

            if (mode == CombineMode.Average)
            {
                for (int i = 0; i < len; i++)
                {
                    float s = 0f;
                    for (int k = 0; k < n; k++) s += stages[k][i];
                    V[i] = Mathf.Clamp01(s / n);
                }
            }
            else if (mode == CombineMode.ThresholdStack)
            {
                for (int i = 0; i < len; i++)
                {
                    int c = 0;
                    for (int k = 0; k < n; k++) if (stages[k][i] >= threshold) c++;
                    V[i] = (float)c / n;
                }
            }
            else // DistanceBlend
            {
                float half = Mathf.Max(0.001f, maxDistPx);
                var acc = new float[len];
                for (int k = 0; k < n; k++)
                {
                    float[] sd = SignedDistance(stages[k], w, h, threshold);
                    for (int i = 0; i < len; i++)
                        acc[i] += Mathf.Clamp01(0.5f + sd[i] / (2f * half));
                }
                for (int i = 0; i < len; i++) V[i] = acc[i] / n;
            }
            return V;
        }

        // ---------------- ノーマルからの段階生成（ライトスイープ） ----------------
        /// <summary>
        /// ノーマルテクスチャからライトを段階的にスイープして、各段階の陰影(0..1)を生成する。
        /// これを Combine に渡すことで、ノーマル＋段階数だけでSDFを作れる。
        /// </summary>
        public static List<float[]> BuildSweepStages(Color[] normal, int sw, int sh, int dw, int dh,
                                                     int steps, float angleMinDeg, float angleMaxDeg,
                                                     float pitchDeg, bool greenFlip,
                                                     float extractThreshold, float feather, bool binarize)
        {
            float th = Mathf.Clamp01(extractThreshold);
            float f = Mathf.Max(1e-4f, feather);
            float hi = Mathf.Min(1f, th + f);
            int len = dw * dh;
            // 法線をデコード＆対象サイズへリサンプル
            var N = new Vector3[len];
            for (int y = 0; y < dh; y++)
            {
                float v = (y + 0.5f) / dh;
                for (int x = 0; x < dw; x++)
                {
                    float u = (x + 0.5f) / dw;
                    Color c = SampleBilinear(normal, sw, sh, u, v);
                    float nx = c.r * 2f - 1f;
                    float ny = c.g * 2f - 1f;
                    float nz = c.b * 2f - 1f;
                    if (greenFlip) ny = -ny;
                    var n = new Vector3(nx, ny, nz);
                    float mag = n.magnitude;
                    N[y * dw + x] = (mag > 1e-6f) ? n / mag : new Vector3(0, 0, 1);
                }
            }

            int n2 = Mathf.Max(1, steps);
            var stages = new List<float[]>(n2);
            for (int i = 0; i < n2; i++)
            {
                float t = (n2 <= 1) ? 0.5f : (float)i / (n2 - 1);
                float a = Mathf.Deg2Rad * Mathf.Lerp(angleMinDeg, angleMaxDeg, t);
                float p = Mathf.Deg2Rad * pitchDeg;
                float cp = Mathf.Cos(p);
                var L = new Vector3(Mathf.Sin(a) * cp, Mathf.Sin(p), Mathf.Cos(a) * cp).normalized;

                var g = new float[len];
                for (int k = 0; k < len; k++)
                {
                    float lit = Mathf.Clamp01(Vector3.Dot(N[k], L)); // 面がライト側を向くほど明るい
                    float e;
                    if (binarize)
                    {
                        e = (lit >= th) ? 1f : 0f; // しきい値で二値化（明るい部分=白）
                    }
                    else
                    {
                        // しきい値以上の明るい部分だけを抽出（輝度を保持、境界はfeatherで柔らかく）
                        float gate = Mathf.SmoothStep(th, hi, lit);
                        e = lit * gate;
                    }
                    g[k] = e;
                }
                stages.Add(g);
            }
            return stages;
        }

        // ---------------- エンコード ----------------
        /// <summary>Unity の EncodeFloatRG 互換。0以上1未満の値を RG にパックして16bit相当の精度に。</summary>
        public static Vector2 EncodeFloatRG(float v)
        {
            v = Mathf.Clamp(v, 0f, 0.9999847f); // 1.0 は frac で0になるため僅かに内側へ
            float x = v;                 // *1
            float y = v * 255f;
            x -= Mathf.Floor(x);
            y -= Mathf.Floor(y);
            x -= y * (1f / 255f);
            return new Vector2(x, y);
        }

        public static Color[] BuildGray8(float[] V)
        {
            var px = new Color[V.Length];
            for (int i = 0; i < V.Length; i++)
            {
                float v = Mathf.Clamp01(V[i]);
                px[i] = new Color(v, v, v, 1f);
            }
            return px;
        }

        public static Color[] BuildPackedRG(float[] V)
        {
            var px = new Color[V.Length];
            for (int i = 0; i < V.Length; i++)
            {
                Vector2 rg = EncodeFloatRG(V[i]);
                px[i] = new Color(rg.x, rg.y, 0f, 1f);
            }
            return px;
        }

        // ---------------- 保存 ----------------
        static Texture2D BuildTexture(Color[] px, int w, int h, bool linear)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static void ImportAndSetSRGB(string absolutePath, bool sRGB)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (!normalized.StartsWith(projectRoot + "/")) return;
            string relative = normalized.Substring(projectRoot.Length + 1);
            AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(relative) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = sRGB;
                importer.SaveAndReimport();
            }
        }

        static bool WriteBytes(string absolutePath, byte[] bytes)
        {
            try
            {
                string dir = Path.GetDirectoryName(absolutePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(absolutePath, bytes);
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SdfTextureGenerator] 保存に失敗: {e.Message}");
                return false;
            }
        }

        /// <summary>SDF値を指定精度で保存。戻り値=成功可否。</summary>
        public static bool Save(float[] V, int w, int h, string absolutePath, OutputPrecision precision)
        {
            if (precision == OutputPrecision.ExrFloat)
            {
                var tex = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                var px = new Color[V.Length];
                for (int i = 0; i < V.Length; i++) { float v = V[i]; px[i] = new Color(v, v, v, 1f); }
                tex.SetPixels(px);
                tex.Apply();
                byte[] exr = tex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
                Object.DestroyImmediate(tex);
                if (!WriteBytes(absolutePath, exr)) return false;
                ImportAndSetSRGB(absolutePath, false);
                return true;
            }
            else
            {
                Color[] px = (precision == OutputPrecision.PackedRG16) ? BuildPackedRG(V) : BuildGray8(V);
                var tex = BuildTexture(px, w, h, /*linear*/ true);
                byte[] png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);
                if (!WriteBytes(absolutePath, png)) return false;
                // データマップなので sRGB OFF
                ImportAndSetSRGB(absolutePath, false);
                return true;
            }
        }
    }
}
