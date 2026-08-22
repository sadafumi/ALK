// AlbedoShadowSurface - ShadowSurfaceCore.cs
// アルベドテクスチャから「影面」テクスチャを生成する中核ロジック。
//   基本    : albedo * multiplier (0..1) の乗算
//   オプション: OKLCH空間で L(明度) を直接減衰させる補正（C彩度・H色相を保持）
// 色変換はCPUで明示的に行い、プロジェクトのカラースペースに依存しない結果を得る。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlbedoShadowSurface
{
    public struct ShadowParams
    {
        public float multiplier;  // 0..1 : 1=元のまま, 0=黒
        public bool useOklch;     // OKLCH補正を使うか
        public float correction;  // 0..1 : 0=RGB乗算のみ, 1=OKLCH-L減衰
        public float chroma;      // 影部の彩度倍率 (1=保持)
        public Color tint;        // 影の色味 (白=無効)

        public static ShadowParams Default => new ShadowParams
        {
            multiplier = 0.5f,
            useOklch = true,
            correction = 1f,
            chroma = 1f,
            tint = Color.white,
        };
    }

    public enum AlphaChannel { R, G, B, A, Luminance }

    public static class ShadowSurfaceCore
    {
        // ---------------- sRGB <-> Linear ----------------
        public static float SrgbToLinear(float c)
        {
            c = Mathf.Clamp01(c);
            return (c <= 0.04045f) ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        public static float LinearToSrgb(float c)
        {
            c = Mathf.Max(0f, c);
            float v = (c <= 0.0031308f) ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
            return Mathf.Clamp01(v);
        }

        // ---------------- linear sRGB <-> OKLab (Björn Ottosson) ----------------
        static Vector3 LinearToOklab(Vector3 c)
        {
            float l = 0.4122214708f * c.x + 0.5363325363f * c.y + 0.0514459929f * c.z;
            float m = 0.2119034982f * c.x + 0.6806995451f * c.y + 0.1073969566f * c.z;
            float s = 0.0883024619f * c.x + 0.2817188376f * c.y + 0.6299787005f * c.z;

            float l_ = Cbrt(l);
            float m_ = Cbrt(m);
            float s_ = Cbrt(s);

            return new Vector3(
                0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_,
                1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_,
                0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_
            );
        }

        static Vector3 OklabToLinear(Vector3 lab)
        {
            float l_ = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float m_ = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float s_ = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;

            float l = l_ * l_ * l_;
            float m = m_ * m_ * m_;
            float s = s_ * s_ * s_;

            return new Vector3(
                +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s
            );
        }

        static float Cbrt(float x)
        {
            if (x < 0f) return -Mathf.Pow(-x, 1f / 3f);
            return Mathf.Pow(x, 1f / 3f);
        }

        // ---------------- ピクセル処理 ----------------
        /// <summary>sRGB入力(0..1)から影面ピクセル(sRGB)を計算。</summary>
        public static Color ProcessPixel(Color a, in ShadowParams p)
        {
            float m = Mathf.Clamp01(p.multiplier);

            // 基本: sRGB空間での単純乗算
            Vector3 naive = new Vector3(a.r * m, a.g * m, a.b * m);

            Vector3 outRgb = naive;

            if (p.useOklch && p.correction > 0f)
            {
                // OKLCH空間で L を直接減衰（a,b を保持 → C・H 保持）
                Vector3 lin = new Vector3(SrgbToLinear(a.r), SrgbToLinear(a.g), SrgbToLinear(a.b));
                Vector3 lab = LinearToOklab(lin);
                lab.x *= m;              // L(明度)を減衰
                lab.y *= p.chroma;       // 彩度(a)倍率
                lab.z *= p.chroma;       // 彩度(b)倍率
                Vector3 linD = OklabToLinear(lab);
                Vector3 oklch = new Vector3(
                    LinearToSrgb(linD.x), LinearToSrgb(linD.y), LinearToSrgb(linD.z));

                outRgb = Vector3.Lerp(naive, oklch, Mathf.Clamp01(p.correction));
            }

            return new Color(
                Mathf.Clamp01(outRgb.x * p.tint.r),
                Mathf.Clamp01(outRgb.y * p.tint.g),
                Mathf.Clamp01(outRgb.z * p.tint.b),
                a.a);
        }

        /// <summary>バッファ全体を処理。alphaOverride(長さ一致)があれば出力Alphaをそれで置き換える。</summary>
        public static Color[] ProcessBuffer(Color[] src, in ShadowParams p, float[] alphaOverride = null)
        {
            var dst = new Color[src.Length];
            bool useA = alphaOverride != null && alphaOverride.Length == src.Length;
            for (int i = 0; i < src.Length; i++)
            {
                Color c = ProcessPixel(src[i], p);
                if (useA) c.a = alphaOverride[i];
                dst[i] = c;
            }
            return dst;
        }

        // ---------------- Alpha用: 白黒テクスチャのチャンネルを対象サイズへサンプル ----------------
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

        static float PickChannel(Color c, AlphaChannel ch)
        {
            switch (ch)
            {
                case AlphaChannel.R: return c.r;
                case AlphaChannel.G: return c.g;
                case AlphaChannel.B: return c.b;
                case AlphaChannel.A: return c.a;
                case AlphaChannel.Luminance: return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            }
            return 0f;
        }

        /// <summary>白黒テクスチャ(src, sw×sh)の指定チャンネルを、dw×dh のAlpha配列へサンプルする。</summary>
        public static float[] BuildAlpha(Color[] src, int sw, int sh, int dw, int dh,
                                         AlphaChannel ch, bool invert)
        {
            var dst = new float[dw * dh];
            for (int y = 0; y < dh; y++)
            {
                float v = (y + 0.5f) / dh;
                for (int x = 0; x < dw; x++)
                {
                    float u = (x + 0.5f) / dw;
                    Color c = SampleBilinear(src, sw, sh, u, v);
                    float val = PickChannel(c, ch);
                    if (invert) val = 1f - val;
                    dst[y * dw + x] = Mathf.Clamp01(val);
                }
            }
            return dst;
        }

        /// <summary>OKLCHの L(知覚明度) をグレースケール化して返す。normalizeでmin-max正規化。</summary>
        public static Color[] BuildOklchGrayscale(Color[] src, bool normalize)
        {
            var L = new float[src.Length];
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < src.Length; i++)
            {
                Vector3 lin = new Vector3(
                    SrgbToLinear(src[i].r), SrgbToLinear(src[i].g), SrgbToLinear(src[i].b));
                float l = LinearToOklab(lin).x;
                L[i] = l;
                if (l < mn) mn = l;
                if (l > mx) mx = l;
            }

            float range = Mathf.Max(1e-6f, mx - mn);
            var dst = new Color[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                float g = normalize ? (L[i] - mn) / range : Mathf.Clamp01(L[i]);
                dst[i] = new Color(g, g, g, src[i].a);
            }
            return dst;
        }

        // ---------------- テクスチャ入出力 ----------------
        /// <summary>読み取り不可テクスチャでもBlit経由でsRGB値(0..1)として取得する。</summary>
        public static Color[] ReadTextureSrgb(Texture src, out int width, out int height)
        {
            width = src.width;
            height = src.height;

            var tmp = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;

            Graphics.Blit(src, tmp);
            RenderTexture.active = tmp;

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, /*linear*/ false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);

            Color[] pixels = tex.GetPixels();
            Object.DestroyImmediate(tex);
            return pixels;
        }

        /// <summary>Color[]から表示・保存用のTexture2Dを生成(sRGB)。</summary>
        public static Texture2D BuildTexture(Color[] pixels, int width, int height)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false, /*linear*/ false);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>最長辺がmaxSize以下になるよう最近傍で縮小(プレビュー用)。</summary>
        public static Color[] Downscale(Color[] src, int w, int h, int maxSize, out int nw, out int nh)
        {
            int longest = Mathf.Max(w, h);
            if (longest <= maxSize)
            {
                nw = w; nh = h;
                return src;
            }
            float scale = (float)maxSize / longest;
            nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            var dst = new Color[nw * nh];
            for (int y = 0; y < nh; y++)
            {
                int sy = Mathf.Min(h - 1, Mathf.FloorToInt((y + 0.5f) / nh * h));
                for (int x = 0; x < nw; x++)
                {
                    int sx = Mathf.Min(w - 1, Mathf.FloorToInt((x + 0.5f) / nw * w));
                    dst[y * nw + x] = src[sy * w + sx];
                }
            }
            return dst;
        }

        /// <summary>Color[]をPNG保存。Assets配下なら自動インポート。</summary>
        public static bool SavePng(Color[] pixels, int width, int height, string absolutePath)
        {
            var tex = BuildTexture(pixels, width, height);
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
                Debug.LogError($"[AlbedoShadowSurface] PNG保存に失敗: {e.Message}");
                return false;
            }

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
