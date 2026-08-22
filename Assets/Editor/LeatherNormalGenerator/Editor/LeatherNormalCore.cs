// LeatherNormalGenerator - LeatherNormalCore.cs
// ライダースジャケット等のレザー質感（シボ・シワ・うねり・微細凹凸）を
// 手続き生成でハイトマップとして合成し、ノーマルマップへ変換する中核ロジック。
//   - シボ: Worley(セルラー)ノイズ。セル内の盛り上がり＋セル境界の溝
//   - シワ: リッジ状fBmノイズ。着用時の折りシワ（異方性対応）
//   - うねり: 低周波fBm。革表面の緩やかな起伏
//   - 微細: 高周波ノイズ。銀面のざらつき
// すべてラップ格子で計算するためシームレスにタイリング可能。
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LeatherNormalGenerator
{
    [System.Serializable]
    public class LeatherParams
    {
        public int seed = 12345;

        // ---- シボ（革の粒状の凹凸） ----
        public int grainCells = 48;          // タイル1辺あたりのセル数（大=細かい）
        public float grainJitter = 0.9f;     // セル配置のランダムさ
        public float grainDome = 0.6f;       // セル内の盛り上がり量
        public float grainVariation = 0.35f; // セルごとの高さばらつき
        public float creaseWidth = 0.15f;    // セル境界の溝幅（セル単位）
        public float creaseDepth = 0.7f;     // 溝の深さ
        public bool grainFineLayer = true;   // 細かいシボを重ねる（二重シボ）

        // ---- シワ（折りジワ・着用ジワ） ----
        public float wrinkleAmount = 0.35f;  // シワの深さ
        public int wrinklePeriod = 6;        // シワの細かさ（タイルあたり周期）
        public int wrinkleOctaves = 4;       // 重ねる周波数の数
        public float wrinkleSharpness = 5f;  // シワの鋭さ
        public float wrinkleAspect = 1.6f;   // 異方性（>1で横方向に走るシワ）

        // ---- うねり（大きな起伏） ----
        public float waveAmount = 0.2f;
        public int wavePeriod = 3;

        // ---- 微細ノイズ（銀面のざらつき） ----
        public float microAmount = 0.08f;
        public int microPeriod = 220;

        // ---- ノーマル変換 ----
        public float normalStrength = 1.5f;  // 凹凸の強さ
        public bool flipGreen = false;       // Green(Y)反転（DirectX系はON）
    }

    public static class LeatherNormalCore
    {
        // ---------------- ハッシュ（シード決定的） ----------------
        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d;
            x ^= x >> 15; x *= 0x846ca68b;
            x ^= x >> 16;
            return x;
        }

        static float Hash01(int x, int y, int seed, int ch)
        {
            unchecked
            {
                uint h = Hash((uint)(x * 73856093) ^ (uint)(y * 19349663)
                            ^ (uint)(seed * 83492791) ^ (uint)(ch * (int)0x9E3779B9));
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        // GLSL互換 smoothstep（Mathf.SmoothStep とは引数の意味が異なるため自前実装）
        static float SStep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / Mathf.Max(1e-6f, e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // ---------------- Worley(セルラー)ノイズ（ラップ格子・タイル可能） ----------------
        /// <summary>最近傍距離F1・第二近傍距離F2（セル単位）と最近傍セルの乱数を返す。</summary>
        static void Worley(float u, float v, int period, float jitter, int seed,
                           out float f1, out float f2, out float cellRand)
        {
            float px = u * period, py = v * period;
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
            f1 = 1e9f; f2 = 1e9f; cellRand = 0f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int gx = cx + ox, gy = cy + oy;
                    // タイリングのためセルIDを周期でラップ（特徴点位置はラップ前の座標基準）
                    int wx = ((gx % period) + period) % period;
                    int wy = ((gy % period) + period) % period;
                    float fx = gx + 0.5f + (Hash01(wx, wy, seed, 0) - 0.5f) * jitter;
                    float fy = gy + 0.5f + (Hash01(wx, wy, seed, 1) - 0.5f) * jitter;
                    float dx = px - fx, dy = py - fy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d < f1) { f2 = f1; f1 = d; cellRand = Hash01(wx, wy, seed, 2); }
                    else if (d < f2) { f2 = d; }
                }
            }
        }

        // ---------------- 値ノイズ（ラップ格子・タイル可能） ----------------
        static float ValueNoise(float u, float v, int periodX, int periodY, int seed)
        {
            float x = u * periodX, y = v * periodY;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            // クインティック補間（格子アーティファクト低減）
            float sx = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f);
            float sy = ty * ty * ty * (ty * (ty * 6f - 15f) + 10f);
            int xa = ((x0 % periodX) + periodX) % periodX;
            int xb = (xa + 1) % periodX;
            int ya = ((y0 % periodY) + periodY) % periodY;
            int yb = (ya + 1) % periodY;
            float v00 = Hash01(xa, ya, seed, 3);
            float v10 = Hash01(xb, ya, seed, 3);
            float v01 = Hash01(xa, yb, seed, 3);
            float v11 = Hash01(xb, yb, seed, 3);
            float a = Mathf.Lerp(v00, v10, sx);
            float b = Mathf.Lerp(v01, v11, sx);
            return Mathf.Lerp(a, b, sy);
        }

        /// <summary>フラクタルノイズ（0..1中心0.5）。周期はオクターブごとに倍加しタイル性を維持。</summary>
        static float Fbm(float u, float v, int periodX, int periodY, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int px = Mathf.Max(1, periodX), py = Mathf.Max(1, periodY);
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * ValueNoise(u, v, px, py, seed + o * 131);
                norm += amp;
                amp *= 0.5f;
                px *= 2; py *= 2;
            }
            return sum / Mathf.Max(1e-6f, norm);
        }

        // ---------------- 勾配(Perlin)ノイズ（ラップ格子・タイル可能） ----------------
        // 値ノイズは等高線が格子に沿って直交パターンになるため、シワには勾配ノイズを使う。
        static float GradNoise(float u, float v, int periodX, int periodY, int seed)
        {
            float x = u * periodX, y = v * periodY;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            float sx = tx * tx * tx * (tx * (tx * 6f - 15f) + 10f);
            float sy = ty * ty * ty * (ty * (ty * 6f - 15f) + 10f);

            float Corner(int cx, int cy, float dx, float dy)
            {
                int wx = ((cx % periodX) + periodX) % periodX;
                int wy = ((cy % periodY) + periodY) % periodY;
                float ang = Hash01(wx, wy, seed, 4) * 2f * Mathf.PI;
                return Mathf.Cos(ang) * dx + Mathf.Sin(ang) * dy;
            }

            float n00 = Corner(x0, y0, tx, ty);
            float n10 = Corner(x0 + 1, y0, tx - 1f, ty);
            float n01 = Corner(x0, y0 + 1, tx, ty - 1f);
            float n11 = Corner(x0 + 1, y0 + 1, tx - 1f, ty - 1f);
            float a = Mathf.Lerp(n00, n10, sx);
            float b = Mathf.Lerp(n01, n11, sx);
            float n = Mathf.Lerp(a, b, sy); // おおよそ ±0.7
            return Mathf.Clamp01(0.5f + n * 0.99f);
        }

        /// <summary>
        /// リッジ状フラクタルノイズ(0..1)。勾配ノイズをオクターブごとにリッジ化してから合成する
        /// （合成後にリッジ化すると値が0.5付近へ集中して線が消えるため）。
        /// さらに低周波ノイズでドメインワープし、蛇行する有機的な折り目にする。
        /// ワープ場も周期的なのでタイル性は保たれる。
        /// </summary>
        static float RidgedFbm(float u, float v, int periodX, int periodY, int octaves,
                               float sharpness, int seed)
        {
            const float kWarp = 0.15f;
            float wu = u + (ValueNoise(u, v, 2, 2, seed + 901) - 0.5f) * kWarp;
            float wv = v + (ValueNoise(u, v, 2, 2, seed + 902) - 0.5f) * kWarp;

            float sum = 0f, amp = 0.5f, norm = 0f;
            int px = Mathf.Max(1, periodX), py = Mathf.Max(1, periodY);
            for (int o = 0; o < octaves; o++)
            {
                float n = GradNoise(wu, wv, px, py, seed + o * 131);
                float r = 1f - Mathf.Abs(2f * n - 1f);
                sum += amp * Mathf.Pow(r, sharpness);
                norm += amp;
                amp *= 0.5f;
                px *= 2; py *= 2;
            }
            return sum / Mathf.Max(1e-6f, norm);
        }

        // ---------------- ハイトマップ生成 ----------------
        /// <summary>レザー質感のハイトマップ(0..1)を生成する。シームレスタイル。</summary>
        public static float[] BuildHeight(int w, int h, LeatherParams p)
        {
            var H = new float[w * h];
            int cells = Mathf.Max(2, p.grainCells);
            int fineCells = cells * 3;
            int wpx = Mathf.Max(1, Mathf.RoundToInt(p.wrinklePeriod * p.wrinkleAspect));
            int wpy = Mathf.Max(1, p.wrinklePeriod);
            int seed = p.seed;

            Parallel.For(0, h, y =>
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;

                    // ---- シボ: セル内ドーム − セル境界の溝 ----
                    Worley(u, v, cells, p.grainJitter, seed, out float f1, out float f2, out float cr);
                    float nd = f1 / 0.75f;
                    float dome = Mathf.Max(0f, 1f - nd * nd); // 放物面状の盛り上がり
                    float amp = 1f - p.grainVariation * cr;    // セルごとの高さばらつき
                    float crease = 1f - SStep(0f, Mathf.Max(1e-3f, p.creaseWidth), f2 - f1);
                    float grain = p.grainDome * dome * amp - p.creaseDepth * crease;

                    // 細かいシボを弱く重ねる（革らしい二重の粒感）
                    if (p.grainFineLayer)
                    {
                        Worley(u, v, fineCells, p.grainJitter, seed + 77, out float g1, out float g2, out _);
                        float fnd = g1 / 0.75f;
                        float fineDome = Mathf.Max(0f, 1f - fnd * fnd);
                        float fineCrease = 1f - SStep(0f, Mathf.Max(1e-3f, p.creaseWidth), g2 - g1);
                        grain += 0.3f * (p.grainDome * fineDome - p.creaseDepth * fineCrease);
                    }

                    // ---- シワ: リッジ状fBm（線状の折り目が谷になる） ----
                    float ridge = RidgedFbm(u, v, wpx, wpy, Mathf.Max(1, p.wrinkleOctaves),
                                            Mathf.Max(0.1f, p.wrinkleSharpness), seed + 101);
                    float wrinkle = -p.wrinkleAmount * ridge;

                    // ---- うねり: 低周波の緩やかな起伏 ----
                    float wave = (Fbm(u, v, p.wavePeriod, p.wavePeriod, 3, seed + 202) - 0.5f) * 2f * p.waveAmount;

                    // ---- 微細: 高周波ざらつき ----
                    float micro = (ValueNoise(u, v, p.microPeriod, p.microPeriod, seed + 303) - 0.5f) * 2f * p.microAmount;

                    float combined = grain * 0.5f + wrinkle + wave + micro;
                    H[y * w + x] = Mathf.Clamp01(0.5f + combined * 0.35f);
                }
            });
            return H;
        }

        // ---------------- ハイト → ノーマル変換 ----------------
        /// <summary>
        /// 中心差分でノーマルマップ(RGB=0..1)へ変換。ラップサンプリングでシームレス。
        /// 強さは512px基準で解像度補正するため、プレビューと保存で見た目が揃う。
        /// </summary>
        public static Color[] HeightToNormal(float[] H, int w, int h, float strength, bool flipGreen)
        {
            var px = new Color[w * h];
            float s = strength * (w / 512f);
            float gy = flipGreen ? -1f : 1f;
            Parallel.For(0, h, y =>
            {
                int yp = (y + 1) % h;
                int ym = (y - 1 + h) % h;
                for (int x = 0; x < w; x++)
                {
                    int xp = (x + 1) % w;
                    int xm = (x - 1 + w) % w;
                    float dhdx = (H[y * w + xp] - H[y * w + xm]) * 0.5f;
                    float dhdy = (H[yp * w + x] - H[ym * w + x]) * 0.5f;
                    var n = new Vector3(-dhdx * s, dhdy * s * gy, 1f).normalized;
                    px[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            });
            return px;
        }

        /// <summary>ハイト値をグレースケール表示用カラーへ。</summary>
        public static Color[] BuildGray(float[] H)
        {
            var px = new Color[H.Length];
            for (int i = 0; i < H.Length; i++)
            {
                float v = Mathf.Clamp01(H[i]);
                px[i] = new Color(v, v, v, 1f);
            }
            return px;
        }

        /// <summary>ノーマルへ指定方向からライトを当てた簡易シェーディング表示（質感確認用）。</summary>
        public static Color[] BuildLit(Color[] normals, float lightAngleDeg, float lightPitchDeg)
        {
            float a = Mathf.Deg2Rad * lightAngleDeg;
            float pch = Mathf.Deg2Rad * lightPitchDeg;
            float cp = Mathf.Cos(pch);
            var L = new Vector3(Mathf.Sin(a) * cp, Mathf.Cos(a) * cp, Mathf.Sin(pch)).normalized;
            var px = new Color[normals.Length];
            for (int i = 0; i < normals.Length; i++)
            {
                Color c = normals[i];
                var n = new Vector3(c.r * 2f - 1f, c.g * 2f - 1f, c.b * 2f - 1f);
                float diff = Mathf.Clamp01(Vector3.Dot(n, L));
                // 弱いスペキュラを足してレザーのツヤ感を確認しやすく
                var V2 = new Vector3(0f, 0f, 1f);
                var half = (L + V2).normalized;
                float spec = Mathf.Pow(Mathf.Clamp01(Vector3.Dot(n, half)), 24f) * 0.35f;
                float lit = Mathf.Clamp01(0.12f + diff * 0.8f + spec);
                px[i] = new Color(lit, lit, lit, 1f);
            }
            return px;
        }

        // ---------------- 保存 ----------------
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
                Debug.LogError($"[LeatherNormalGenerator] 保存に失敗: {e.Message}");
                return false;
            }
        }

        /// <summary>プロジェクト内パスならインポートしてノーマルマップとして設定する。</summary>
        static void ImportAsNormalMap(string absolutePath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (!normalized.StartsWith(projectRoot + "/")) return;
            string relative = normalized.Substring(projectRoot.Length + 1);
            AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(relative) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.SaveAndReimport();
            }
        }

        /// <summary>ノーマルマップをPNG保存し、インポート設定をNormalMapにする。戻り値=成功可否。</summary>
        public static bool SaveNormal(Color[] px, int w, int h, string absolutePath)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, /*linear*/ true);
            tex.SetPixels(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (!WriteBytes(absolutePath, png)) return false;
            ImportAsNormalMap(absolutePath);
            return true;
        }

        /// <summary>ハイトマップをグレースケールPNG保存（sRGB OFF）。戻り値=成功可否。</summary>
        public static bool SaveHeight(float[] H, int w, int h, string absolutePath)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, /*linear*/ true);
            tex.SetPixels(BuildGray(H));
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            if (!WriteBytes(absolutePath, png)) return false;

            string projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string normalized = absolutePath.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/"))
            {
                string relative = normalized.Substring(projectRoot.Length + 1);
                AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(relative) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = false;
                    importer.wrapMode = TextureWrapMode.Repeat;
                    importer.SaveAndReimport();
                }
            }
            return true;
        }
    }
}
