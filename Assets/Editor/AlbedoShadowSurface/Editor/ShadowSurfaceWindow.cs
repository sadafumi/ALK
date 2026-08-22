// AlbedoShadowSurface - ShadowSurfaceWindow.cs
// メニュー: Tools > Albedo Shadow Surface
// アルベドテクスチャから影面テクスチャを生成する。基本はalbedo×0..1の乗算、
// オプションでOKLCH空間のL減衰による知覚的な暗さ補正を掛ける。
using UnityEditor;
using UnityEngine;

namespace AlbedoShadowSurface
{
    public class ShadowSurfaceWindow : EditorWindow
    {
        const int kPreviewMax = 512;

        Texture2D _albedo;
        ShadowParams _params = ShadowParams.Default;

        // グレースケール出力オプション
        bool _exportGrayscale = false;
        bool _grayscaleNormalize = false;

        // Alpha出力（白黒テクスチャをそのままAlphaへ）
        Texture2D _alphaTex;
        AlphaChannel _alphaChannel = AlphaChannel.Luminance;
        bool _alphaInvert = false;
        Color[] _alphaSrc;  int _alphaSrcW, _alphaSrcH;

        // キャッシュ
        Color[] _srcFull;   int _fullW, _fullH;
        Color[] _srcPrev;   int _prevW, _prevH;
        Texture2D _previewResult;
        Texture2D _previewGray;
        Texture2D _previewAlpha;
        Vector2 _scroll;
        bool _dirty;

        [MenuItem("Tools/Albedo Shadow Surface")]
        public static void Open()
        {
            var win = GetWindow<ShadowSurfaceWindow>("Shadow Surface");
            win.minSize = new Vector2(360, 600);
            win.Show();
        }

        void OnEnable() { Persist(true); }

        void OnDisable()
        {
            Persist(false);
            DestroyTex(ref _previewResult);
            DestroyTex(ref _previewGray);
            DestroyTex(ref _previewAlpha);
        }

        // ---- パラメータの保持（EditorPrefs） ----
        static float Pf(string k, float v, bool load) { if (load) return EditorPrefs.GetFloat(k, v); EditorPrefs.SetFloat(k, v); return v; }
        static int Pi(string k, int v, bool load) { if (load) return EditorPrefs.GetInt(k, v); EditorPrefs.SetInt(k, v); return v; }
        static bool Pb(string k, bool v, bool load) { if (load) return EditorPrefs.GetBool(k, v); EditorPrefs.SetBool(k, v); return v; }

        void Persist(bool load)
        {
            const string P = "ShadowSurface.";
            _params.multiplier = Pf(P + "mul", _params.multiplier, load);
            _params.useOklch = Pb(P + "oklch", _params.useOklch, load);
            _params.correction = Pf(P + "corr", _params.correction, load);
            _params.chroma = Pf(P + "chroma", _params.chroma, load);
            _params.tint.r = Pf(P + "tintR", _params.tint.r, load);
            _params.tint.g = Pf(P + "tintG", _params.tint.g, load);
            _params.tint.b = Pf(P + "tintB", _params.tint.b, load);
            _params.tint.a = Pf(P + "tintA", _params.tint.a, load);
            _exportGrayscale = Pb(P + "gray", _exportGrayscale, load);
            _grayscaleNormalize = Pb(P + "grayNorm", _grayscaleNormalize, load);
            _alphaChannel = (AlphaChannel)Pi(P + "aCh", (int)_alphaChannel, load);
            _alphaInvert = Pb(P + "aInv", _alphaInvert, load);
            _chooseLocation = Pb(P + "chooseLoc", _chooseLocation, load);
        }

        static void DestroyTex(ref Texture2D t)
        {
            if (t != null) { Object.DestroyImmediate(t); t = null; }
        }

        void LoadAlbedo()
        {
            _srcFull = null; _srcPrev = null;
            if (_albedo == null) return;

            _srcFull = ShadowSurfaceCore.ReadTextureSrgb(_albedo, out _fullW, out _fullH);
            _srcPrev = ShadowSurfaceCore.Downscale(_srcFull, _fullW, _fullH, kPreviewMax, out _prevW, out _prevH);
            _dirty = true;
        }

        void LoadAlphaSource()
        {
            _alphaSrc = (_alphaTex != null)
                ? ShadowSurfaceCore.ReadTextureSrgb(_alphaTex, out _alphaSrcW, out _alphaSrcH)
                : null;
            _dirty = true;
        }

        void RebuildPreview()
        {
            if (_srcPrev == null) return;

            float[] alphaPrev = (_alphaSrc != null)
                ? ShadowSurfaceCore.BuildAlpha(_alphaSrc, _alphaSrcW, _alphaSrcH, _prevW, _prevH, _alphaChannel, _alphaInvert)
                : null;

            var outPix = ShadowSurfaceCore.ProcessBuffer(_srcPrev, _params, alphaPrev);
            DestroyTex(ref _previewResult);
            _previewResult = ShadowSurfaceCore.BuildTexture(outPix, _prevW, _prevH);
            _previewResult.filterMode = FilterMode.Bilinear;

            DestroyTex(ref _previewGray);
            if (_exportGrayscale)
            {
                var gray = ShadowSurfaceCore.BuildOklchGrayscale(_srcPrev, _grayscaleNormalize);
                _previewGray = ShadowSurfaceCore.BuildTexture(gray, _prevW, _prevH);
                _previewGray.filterMode = FilterMode.Bilinear;
            }

            DestroyTex(ref _previewAlpha);
            if (alphaPrev != null)
            {
                var ap = new Color[alphaPrev.Length];
                for (int i = 0; i < ap.Length; i++)
                {
                    float a = alphaPrev[i];
                    ap[i] = new Color(a, a, a, 1f);
                }
                _previewAlpha = ShadowSurfaceCore.BuildTexture(ap, _prevW, _prevH);
                _previewAlpha.filterMode = FilterMode.Bilinear;
            }
            _dirty = false;
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("入力", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _albedo = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("アルベドテクスチャ"), _albedo, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
                LoadAlbedo();

            if (_albedo == null)
            {
                EditorGUILayout.HelpBox("アルベドテクスチャを指定してください。読み取り不可(Read/Write Disabled)でも動作します。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("サイズ", $"{_fullW} x {_fullH}");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("基本の乗算", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _params.multiplier = EditorGUILayout.Slider(
                new GUIContent("乗算値 (0=黒, 1=元のまま)"), _params.multiplier, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("OKLCH補正 (オプション)", EditorStyles.boldLabel);
            _params.useOklch = EditorGUILayout.Toggle(
                new GUIContent("OKLCH補正を使う", "OKLCH空間でL(明度)を減衰。彩度C・色相Hを保持し、色がくすまない影に。"),
                _params.useOklch);

            using (new EditorGUI.DisabledScope(!_params.useOklch))
            {
                _params.correction = EditorGUILayout.Slider(
                    new GUIContent("補正量", "0=単純RGB乗算 / 1=OKLCH-L減衰"), _params.correction, 0f, 1f);
                _params.chroma = EditorGUILayout.Slider(
                    new GUIContent("影部の彩度", "1=保持 / <1でくすませ / >1で鮮やかに"), _params.chroma, 0f, 2f);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("色味 (オプション)", EditorStyles.boldLabel);
            _params.tint = EditorGUILayout.ColorField(
                new GUIContent("影のティント", "白=無効。影に色を乗せたい場合に。"), _params.tint);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Alpha出力（白黒テクスチャ, オプション）", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _alphaTex = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("Alpha用テクスチャ", "指定した白黒テクスチャのチャンネルを出力のAlphaへ書き込む"),
                _alphaTex, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
                LoadAlphaSource();
            using (new EditorGUI.DisabledScope(_alphaTex == null))
            {
                _alphaChannel = (AlphaChannel)EditorGUILayout.EnumPopup(new GUIContent("読取チャンネル"), _alphaChannel);
                _alphaInvert = EditorGUILayout.Toggle(new GUIContent("反転"), _alphaInvert);
            }
            if (_alphaTex == null)
                EditorGUILayout.HelpBox("未指定なら Alpha は元アルベドのAlphaのままです。", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("OKLCHグレースケール出力 (オプション)", EditorStyles.boldLabel);
            _exportGrayscale = EditorGUILayout.Toggle("Lグレースケールを生成", _exportGrayscale);
            using (new EditorGUI.DisabledScope(!_exportGrayscale))
            {
                _grayscaleNormalize = EditorGUILayout.Toggle(
                    new GUIContent("min-max正規化"), _grayscaleNormalize);
            }
            if (EditorGUI.EndChangeCheck())
                _dirty = true;

            if (_dirty)
                RebuildPreview();

            // ---- プレビュー ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 30f, 260f);

            if (_albedo != null)
            {
                EditorGUILayout.LabelField("元アルベド");
                Rect r0 = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r0, _albedo, null, ScaleMode.ScaleToFit);
            }
            if (_previewResult != null)
            {
                EditorGUILayout.LabelField("影面テクスチャ");
                Rect r1 = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r1, _previewResult, null, ScaleMode.ScaleToFit);
            }
            if (_previewGray != null)
            {
                EditorGUILayout.LabelField("OKLCH Lグレースケール");
                Rect r2 = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r2, _previewGray, null, ScaleMode.ScaleToFit);
            }
            if (_previewAlpha != null)
            {
                EditorGUILayout.LabelField("Alpha (グレー表示)");
                Rect ra = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(ra, _previewAlpha, null, ScaleMode.ScaleToFit);
            }
            EditorGUILayout.HelpBox("プレビューは軽量化のため縮小表示です。保存は元解像度で処理します。", MessageType.None);

            // ---- 保存 ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("保存", EditorStyles.boldLabel);
            _chooseLocation = EditorGUILayout.ToggleLeft("保存時に場所を選ぶ（OFFで前回パスに上書き）", _chooseLocation);
            using (new EditorGUI.DisabledScope(_srcFull == null))
            {
                if (GUILayout.Button(SaveButtonLabel("Shadow", "影面テクスチャをPNG保存"), GUILayout.Height(28)))
                    SaveShadow();

                using (new EditorGUI.DisabledScope(!_exportGrayscale))
                {
                    if (GUILayout.Button(SaveButtonLabel("OKLCH_L", "OKLCHグレースケールをPNG保存")))
                        SaveGrayscale();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        // 保存先ディレクトリの記憶（3ツール共通キー）
        const string kLastSaveDirKey = "TextureTools.LastSaveDir";
        bool _chooseLocation = false;
        static string SavePathKey(string suffix) => "ShadowSurface.LastSavePath." + suffix;
        string SaveButtonLabel(string suffix, string fallback)
        {
            string p = EditorPrefs.GetString(SavePathKey(suffix), "");
            if (!_chooseLocation && !string.IsNullOrEmpty(p) && System.IO.File.Exists(p))
                return $"上書き保存 ({System.IO.Path.GetFileName(p)})";
            return fallback;
        }
        string ResolveSavePath(string suffix, string title, string defName)
        {
            string key = SavePathKey(suffix);
            string path = null;
            if (!_chooseLocation)
            {
                path = EditorPrefs.GetString(key, "");
                if (!string.IsNullOrEmpty(path) && !System.IO.File.Exists(path)) path = null;
            }
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel(title, GetLastSaveDir(), defName, "png");
                if (string.IsNullOrEmpty(path)) return null;
            }
            SetLastSaveDir(path);
            EditorPrefs.SetString(key, path);
            return path;
        }
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

        void SaveShadow()
        {
            if (_srcFull == null) return;
            string def = _albedo.name + "_Shadow.png";
            string path = ResolveSavePath("Shadow", "影面テクスチャを保存", def);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                EditorUtility.DisplayProgressBar("Albedo Shadow Surface", "影面を生成中...", 0.4f);
                float[] alphaFull = (_alphaSrc != null)
                    ? ShadowSurfaceCore.BuildAlpha(_alphaSrc, _alphaSrcW, _alphaSrcH, _fullW, _fullH, _alphaChannel, _alphaInvert)
                    : null;
                var outPix = ShadowSurfaceCore.ProcessBuffer(_srcFull, _params, alphaFull);
                if (ShadowSurfaceCore.SavePng(outPix, _fullW, _fullH, path))
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[AlbedoShadowSurface] 保存しました: {path}");
                    EditorUtility.RevealInFinder(path);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        void SaveGrayscale()
        {
            if (_srcFull == null) return;
            string def = _albedo.name + "_OKLCH_L.png";
            string path = ResolveSavePath("OKLCH_L", "OKLCHグレースケールを保存", def);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                EditorUtility.DisplayProgressBar("Albedo Shadow Surface", "グレースケールを生成中...", 0.4f);
                var gray = ShadowSurfaceCore.BuildOklchGrayscale(_srcFull, _grayscaleNormalize);
                if (ShadowSurfaceCore.SavePng(gray, _fullW, _fullH, path))
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[AlbedoShadowSurface] 保存しました: {path}");
                    EditorUtility.RevealInFinder(path);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }
}
