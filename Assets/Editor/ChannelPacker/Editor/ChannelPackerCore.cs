// ChannelPacker - ChannelPackerCore.cs
// R/G/B/A それぞれに別々のグレースケール(テクスチャ)を割り当て、1枚のテクスチャに合成する中核ロジック。
// 各スロットは「どのテクスチャの・どのチャンネル(R/G/B/A/輝度)を・反転するか・未指定時の定数」を指定できる。
// 色変換に依存する処理は最小限とし、値はストア値(0..1)としてそのまま扱う。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ChannelPacker
{
    public enum SourceChannel
    {
        R = 0,
        G = 1,
        B = 2,
        A = 3,
        Luminance = 4,
    }

    public enum OutputColorSpace
    {
        Linear = 0, // マスク/データマップ向け (sRGB OFF 推奨)
        sRGB = 1,   // 見た目重視の合成向け
    }

    /// <summary>読み込んだソーステクスチャのピクセルバッファ。</summary>
    public class SourceBuffer
    {
        public Color[] pixels;
        public int width;
        public int height;
    }

    /// <summary>1チャンネル分の入力設定。</summary>
    public struct SlotInput
    {
        public SourceBuffer buffer;   // null なら constant を使用
        public SourceChannel channel;
        public bool invert;
        public float constant;        // 未指定時 / テクスチャなし時の値
    }

    public static class ChannelPackerCore
    {
        // ---------------- サンプリング ----------------
        static Color SampleBilinear(SourceBuffer b, float u, float v)
        {
            float fx = Mathf.Clamp01(u) * b.width - 0.5f;
            float fy = Mathf.Clamp01(v) * b.height - 0.5f;

            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            int x1 = Mathf.Clamp(x0 + 1, 0, b.width - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, b.height - 1);
            x0 = Mathf.Clamp(x0, 0, b.width - 1);
            y0 = Mathf.Clamp(y0, 0, b.height - 1);

            Color c00 = b.pixels[y0 * b.width + x0];
            Color c10 = b.pixels[y0 * b.width + x1];
            Color c01 = b.pixels[y1 * b.width + x0];
            Color c11 = b.pixels[y1 * b.width + x1];

            Color a = Color.Lerp(c00, c10, tx);
            Color c = Color.Lerp(c01, c11, tx);
            return Color.Lerp(a, c, ty);
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

        /// <summary>1スロットの値を uv 位置から取得。</summary>
        static float SampleSlot(in SlotInput slot, float u, float v)
        {
            float val;
            if (slot.buffer != null)
            {
                Color c = SampleBilinear(slot.buffer, u, v);
                val = PickChannel(c, slot.channel);
            }
            else
            {
                val = slot.constant;
            }
            if (slot.invert) val = 1f - val;
            return Mathf.Clamp01(val);
        }

        /// <summary>4スロットを合成した Color[] を返す。slots は [R,G,B,A] の順。</summary>
        public static Color[] Pack(SlotInput[] slots, int outW, int outH)
        {
            var dst = new Color[outW * outH];
            for (int y = 0; y < outH; y++)
            {
                float v = (y + 0.5f) / outH;
                for (int x = 0; x < outW; x++)
                {
                    float u = (x + 0.5f) / outW;
                    float r = SampleSlot(slots[0], u, v);
                    float g = SampleSlot(slots[1], u, v);
                    float b = SampleSlot(slots[2], u, v);
                    float a = SampleSlot(slots[3], u, v);
                    dst[y * outW + x] = new Color(r, g, b, a);
                }
            }
            return dst;
        }

        // ---------------- 入出力 ----------------
        /// <summary>読み取り不可テクスチャでもBlit経由でストア値(0..1)として取得する。</summary>
        public static SourceBuffer ReadTexture(Texture src)
        {
            int w = src.width;
            int h = src.height;

            var tmp = RenderTexture.GetTemporary(
                w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;

            Graphics.Blit(src, tmp);
            RenderTexture.active = tmp;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, /*linear*/ false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(tmp);

            var buf = new SourceBuffer { pixels = tex.GetPixels(), width = w, height = h };
            Object.DestroyImmediate(tex);
            return buf;
        }

        public static Texture2D BuildTexture(Color[] pixels, int w, int h, bool linear)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>PNG保存。Assets配下ならインポートし、カラースペースに応じてsRGBフラグを設定する。</summary>
        public static bool SavePng(Color[] pixels, int w, int h, string absolutePath, OutputColorSpace space)
        {
            bool linear = (space == OutputColorSpace.Linear);
            var tex = BuildTexture(pixels, w, h, linear);
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
                Debug.LogError($"[ChannelPacker] PNG保存に失敗: {e.Message}");
                return false;
            }

            // Assets 配下ならインポートし、sRGB フラグを出力設定に合わせる
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/"))
            {
                string relative = normalized.Substring(projectRoot.Length + 1);
                AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);

                var importer = AssetImporter.GetAtPath(relative) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = (space == OutputColorSpace.sRGB);
                    importer.SaveAndReimport();
                }
            }
            return true;
        }
    }
}
