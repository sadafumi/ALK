// RampMapGenerator - RampMapWindow.cs
// メニュー: Tools > Ramp Map Generator
// 2つのGradientを使い、RGBチャンネルとAlphaチャンネルに別々の情報を持つRamp(グラデーション)マップを生成する。
//   RGB = Gradient1 の色 / A = Gradient2 から取り出した1チャンネル値
using UnityEditor;
using UnityEngine;

namespace RampMapGenerator
{
    public enum AlphaSource { Luminance, R, G, B, GradientAlpha }
    public enum RampDirection { Horizontal, Vertical }

    public class RampMapWindow : EditorWindow
    {
        [SerializeField] Gradient _rgbGradient;
        [SerializeField] Gradient _alphaGradient;
        [SerializeField] AlphaSource _alphaSource = AlphaSource.Luminance;
        [SerializeField] RampDirection _direction = RampDirection.Horizontal;
        [SerializeField] int _width = 256;
        [SerializeField] int _height = 16;
        [SerializeField] bool _sRGB = true;
        [SerializeField] bool _chooseLocation = false;

        Texture2D _previewTex;
        Texture2D _alphaPreview;
        Vector2 _scroll;
        bool _dirty = true;

        [MenuItem("Tools/Ramp Map Generator")]
        public static void Open()
        {
            var win = GetWindow<RampMapWindow>("Ramp Map");
            win.minSize = new Vector2(360, 480);
            win.Show();
        }

        void OnEnable()
        {
            if (_rgbGradient == null) _rgbGradient = DefaultGradient(Color.black, Color.white);
            if (_alphaGradient == null) _alphaGradient = DefaultGradient(Color.black, Color.white);
            Persist(true);
            _dirty = true;
        }

        void OnDisable()
        {
            Persist(false);
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }
            if (_alphaPreview != null) { Object.DestroyImmediate(_alphaPreview); _alphaPreview = null; }
        }

        // ---- パラメータの保持（EditorPrefs） ----
        [System.Serializable] class GradWrap { public Gradient g; }
        static int Pi(string k, int v, bool load) { if (load) return EditorPrefs.GetInt(k, v); EditorPrefs.SetInt(k, v); return v; }
        static bool Pb(string k, bool v, bool load) { if (load) return EditorPrefs.GetBool(k, v); EditorPrefs.SetBool(k, v); return v; }
        static void SaveGrad(string k, Gradient g) { EditorPrefs.SetString(k, EditorJsonUtility.ToJson(new GradWrap { g = g })); }
        static Gradient LoadGrad(string k, Gradient def)
        {
            string s = EditorPrefs.GetString(k, "");
            if (string.IsNullOrEmpty(s)) return def;
            var w = new GradWrap { g = new Gradient() };
            try { EditorJsonUtility.FromJsonOverwrite(s, w); } catch { return def; }
            return w.g ?? def;
        }

        void Persist(bool load)
        {
            const string P = "RampMap.";
            _alphaSource = (AlphaSource)Pi(P + "aSrc", (int)_alphaSource, load);
            _direction = (RampDirection)Pi(P + "dir", (int)_direction, load);
            _width = Pi(P + "w", _width, load);
            _height = Pi(P + "h", _height, load);
            _sRGB = Pb(P + "srgb", _sRGB, load);
            _chooseLocation = Pb(P + "chooseLoc", _chooseLocation, load);
            if (load)
            {
                _rgbGradient = LoadGrad(P + "gRGB", _rgbGradient);
                _alphaGradient = LoadGrad(P + "gA", _alphaGradient);
            }
            else
            {
                SaveGrad(P + "gRGB", _rgbGradient);
                SaveGrad(P + "gA", _alphaGradient);
            }
        }

        static Gradient DefaultGradient(Color a, Color b)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        float AlphaFrom(Color c)
        {
            switch (_alphaSource)
            {
                case AlphaSource.R: return c.r;
                case AlphaSource.G: return c.g;
                case AlphaSource.B: return c.b;
                case AlphaSource.GradientAlpha: return c.a;
                default: return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; // Luminance
            }
        }

        Color[] BuildPixels(int w, int h)
        {
            var px = new Color[w * h];
            for (int i = 0; i < (_direction == RampDirection.Horizontal ? w : h); i++)
            {
                int n = (_direction == RampDirection.Horizontal ? w : h);
                float t = (n <= 1) ? 0f : (float)i / (n - 1);
                Color rgb = _rgbGradient.Evaluate(t);
                float a = Mathf.Clamp01(AlphaFrom(_alphaGradient.Evaluate(t)));
                Color col = new Color(rgb.r, rgb.g, rgb.b, a);

                if (_direction == RampDirection.Horizontal)
                    for (int y = 0; y < h; y++) px[y * w + i] = col;
                else
                    for (int x = 0; x < w; x++) px[i * w + x] = col;
            }
            return px;
        }

        void RebuildPreview()
        {
            _dirty = false;
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }

            int w = Mathf.Clamp(_width, 1, 8192);
            int h = Mathf.Clamp(_height, 1, 8192);
            var px = BuildPixels(w, h);
            _previewTex = new Texture2D(w, h, TextureFormat.RGBA32, false, !_sRGB);
            _previewTex.wrapMode = TextureWrapMode.Clamp;
            _previewTex.filterMode = FilterMode.Bilinear;
            _previewTex.SetPixels(px);
            _previewTex.Apply();

            // Alpha可視化
            if (_alphaPreview != null) { Object.DestroyImmediate(_alphaPreview); _alphaPreview = null; }
            var g = new Color[px.Length];
            for (int i = 0; i < px.Length; i++) { float a = px[i].a; g[i] = new Color(a, a, a, 1f); }
            _alphaPreview = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            _alphaPreview.wrapMode = TextureWrapMode.Clamp;
            _alphaPreview.filterMode = FilterMode.Bilinear;
            _alphaPreview.SetPixels(g);
            _alphaPreview.Apply();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Gradient", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _rgbGradient = EditorGUILayout.GradientField(
                new GUIContent("RGB用 Gradient", "このGradientの色を RGB に使用"), _rgbGradient);
            _alphaGradient = EditorGUILayout.GradientField(
                new GUIContent("Alpha用 Gradient", "このGradientから1チャンネル取り出して A に使用"), _alphaGradient);
            _alphaSource = (AlphaSource)EditorGUILayout.EnumPopup(
                new GUIContent("Alphaの取り出し元", "Alpha用Gradientのどの値をAlphaに入れるか"), _alphaSource);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出力", EditorStyles.boldLabel);
            _direction = (RampDirection)EditorGUILayout.EnumPopup(new GUIContent("方向"), _direction);
            _width = Mathf.Max(1, EditorGUILayout.IntField("幅", _width));
            _height = Mathf.Max(1, EditorGUILayout.IntField("高さ", _height));
            _sRGB = EditorGUILayout.Toggle(
                new GUIContent("sRGB", "色ランプはON推奨。データとして使う場合はOFF(Linear)"), _sRGB);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            if (_dirty) RebuildPreview();

            // ---- プレビュー ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            if (_previewTex != null)
            {
                float w = EditorGUIUtility.currentViewWidth - 30f;
                EditorGUILayout.LabelField("RGB");
                Rect rRGB = GUILayoutUtility.GetRect(w, 40f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawPreviewTexture(rRGB, _previewTex, null, ScaleMode.StretchToFill, 0f);

                if (_alphaPreview != null)
                {
                    EditorGUILayout.LabelField("Alpha (グレー表示)");
                    Rect rA = GUILayoutUtility.GetRect(w, 40f, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawPreviewTexture(rA, _alphaPreview, null, ScaleMode.StretchToFill, 0f);
                }
            }

            // ---- 保存 ----
            EditorGUILayout.Space();
            _chooseLocation = EditorGUILayout.ToggleLeft("保存時に場所を選ぶ（OFFで前回パスに上書き）", _chooseLocation);
            string ovPath = EditorPrefs.GetString(kSavePathKey, "");
            bool hasOv = !_chooseLocation && !string.IsNullOrEmpty(ovPath) && System.IO.File.Exists(ovPath);
            string saveLabel = hasOv ? $"上書き保存 ({System.IO.Path.GetFileName(ovPath)})" : "Rampマップを保存 (PNG)";
            if (GUILayout.Button(saveLabel, GUILayout.Height(28)))
                Save();

            EditorGUILayout.EndScrollView();
        }

        void Save()
        {
            int w = Mathf.Clamp(_width, 1, 8192);
            int h = Mathf.Clamp(_height, 1, 8192);
            string path = ResolveSavePath("Rampマップを保存", "RampMap.png");
            if (string.IsNullOrEmpty(path)) return;

            var px = BuildPixels(w, h);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !_sRGB);
            tex.SetPixels(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(path, png);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[RampMapGenerator] 保存に失敗: {e.Message}");
                return;
            }

            // Assets配下ならインポートし、sRGB/wrap を設定
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith(projectRoot + "/"))
            {
                string relative = normalized.Substring(projectRoot.Length + 1);
                AssetDatabase.ImportAsset(relative, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(relative) as TextureImporter;
                if (importer != null)
                {
                    importer.sRGBTexture = _sRGB;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.alphaIsTransparency = false;
                    importer.SaveAndReimport();
                }
            }
            AssetDatabase.Refresh();
            Debug.Log($"[RampMapGenerator] 保存しました: {path} ({w}x{h})");
            EditorUtility.RevealInFinder(path);
        }

        // 保存先ディレクトリの記憶（共通キー）
        const string kLastSaveDirKey = "TextureTools.LastSaveDir";
        const string kSavePathKey = "RampMap.LastSavePath";
        static string GetLastSaveDir()
        {
            string d = EditorPrefs.GetString(kLastSaveDirKey, "");
            if (!string.IsNullOrEmpty(d) && System.IO.Directory.Exists(d)) return d;
            return Application.dataPath;
        }
        static void SetLastSaveDir(string filePath)
        {
            string dir = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) EditorPrefs.SetString(kLastSaveDirKey, dir);
        }

        // 前回保存パスを覚えて上書き（_chooseLocation ONでダイアログ）
        string ResolveSavePath(string title, string defName)
        {
            string path = null;
            if (!_chooseLocation)
            {
                path = EditorPrefs.GetString(kSavePathKey, "");
                if (!string.IsNullOrEmpty(path) && !System.IO.File.Exists(path)) path = null;
            }
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel(title, GetLastSaveDir(), defName, "png");
                if (string.IsNullOrEmpty(path)) return null;
            }
            SetLastSaveDir(path);
            EditorPrefs.SetString(kSavePathKey, path);
            return path;
        }
    }
}
