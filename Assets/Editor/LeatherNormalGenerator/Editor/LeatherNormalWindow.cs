// LeatherNormalGenerator - LeatherNormalWindow.cs
// メニュー: Tools > Leather Normal Generator
// ライダースジャケット等のレザー質感（シボ・シワ・うねり・微細凹凸）を
// 手続き生成してノーマルマップとして書き出すエディタツール。
using UnityEditor;
using UnityEngine;

namespace LeatherNormalGenerator
{
    public class LeatherNormalWindow : EditorWindow
    {
        const int kPreviewRes = 512;

        readonly LeatherParams _p = new LeatherParams();

        enum Resolution { _256 = 256, _512 = 512, _1024 = 1024, _2048 = 2048, _4096 = 4096 }
        Resolution _resolution = Resolution._1024;

        enum PreviewMode { ノーマル, ハイト, ライティング }
        PreviewMode _previewMode = PreviewMode.ノーマル;
        float _lightAngle = 45f;

        bool _saveHeightToo = false;
        bool _chooseLocation = false;

        Texture2D _previewTex;
        Vector2 _scroll;
        bool _dirty = true;

        [MenuItem("Tools/Leather Normal Generator")]
        public static void Open()
        {
            var win = GetWindow<LeatherNormalWindow>("Leather Normal");
            win.minSize = new Vector2(380, 720);
            win.Show();
        }

        void OnEnable() { Persist(true); _dirty = true; }

        void OnDisable()
        {
            Persist(false);
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }
        }

        // ---- パラメータの保持（EditorPrefs） ----
        static float Pf(string k, float v, bool load) { if (load) return EditorPrefs.GetFloat(k, v); EditorPrefs.SetFloat(k, v); return v; }
        static int Pi(string k, int v, bool load) { if (load) return EditorPrefs.GetInt(k, v); EditorPrefs.SetInt(k, v); return v; }
        static bool Pb(string k, bool v, bool load) { if (load) return EditorPrefs.GetBool(k, v); EditorPrefs.SetBool(k, v); return v; }

        void Persist(bool load)
        {
            const string P = "LeatherNormal.";
            _p.seed = Pi(P + "seed", _p.seed, load);
            _p.grainCells = Pi(P + "gCells", _p.grainCells, load);
            _p.grainJitter = Pf(P + "gJit", _p.grainJitter, load);
            _p.grainDome = Pf(P + "gDome", _p.grainDome, load);
            _p.grainVariation = Pf(P + "gVar", _p.grainVariation, load);
            _p.creaseWidth = Pf(P + "cW", _p.creaseWidth, load);
            _p.creaseDepth = Pf(P + "cD", _p.creaseDepth, load);
            _p.grainFineLayer = Pb(P + "gFine", _p.grainFineLayer, load);
            _p.wrinkleAmount = Pf(P + "wAmt", _p.wrinkleAmount, load);
            _p.wrinklePeriod = Pi(P + "wPer", _p.wrinklePeriod, load);
            _p.wrinkleOctaves = Pi(P + "wOct", _p.wrinkleOctaves, load);
            _p.wrinkleSharpness = Pf(P + "wShp", _p.wrinkleSharpness, load);
            _p.wrinkleAspect = Pf(P + "wAsp", _p.wrinkleAspect, load);
            _p.waveAmount = Pf(P + "uAmt", _p.waveAmount, load);
            _p.wavePeriod = Pi(P + "uPer", _p.wavePeriod, load);
            _p.microAmount = Pf(P + "mAmt", _p.microAmount, load);
            _p.microPeriod = Pi(P + "mPer", _p.microPeriod, load);
            _p.normalStrength = Pf(P + "nStr", _p.normalStrength, load);
            _p.flipGreen = Pb(P + "nFlip", _p.flipGreen, load);
            _resolution = (Resolution)Pi(P + "res", (int)_resolution, load);
            _previewMode = (PreviewMode)Pi(P + "pvMode", (int)_previewMode, load);
            _lightAngle = Pf(P + "pvLight", _lightAngle, load);
            _saveHeightToo = Pb(P + "saveH", _saveHeightToo, load);
            _chooseLocation = Pb(P + "chooseLoc", _chooseLocation, load);
        }

        // ---- プリセット ----
        void PresetShrink() // シボ強め（シュリンクレザー）
        {
            _p.grainCells = 40; _p.grainDome = 0.75f; _p.grainVariation = 0.45f;
            _p.creaseWidth = 0.18f; _p.creaseDepth = 0.85f; _p.grainFineLayer = true;
            _p.wrinkleAmount = 0.25f; _p.wrinkleSharpness = 5f;
            _p.waveAmount = 0.15f; _p.microAmount = 0.06f; _p.normalStrength = 1.8f;
        }

        void PresetSmooth() // スムースレザー（キメ細かい）
        {
            _p.grainCells = 96; _p.grainDome = 0.35f; _p.grainVariation = 0.25f;
            _p.creaseWidth = 0.12f; _p.creaseDepth = 0.4f; _p.grainFineLayer = true;
            _p.wrinkleAmount = 0.3f; _p.wrinkleSharpness = 6f;
            _p.waveAmount = 0.2f; _p.microAmount = 0.05f; _p.normalStrength = 1.2f;
        }

        void PresetVintage() // ヴィンテージ（シワ・うねり強め）
        {
            _p.grainCells = 56; _p.grainDome = 0.55f; _p.grainVariation = 0.5f;
            _p.creaseWidth = 0.16f; _p.creaseDepth = 0.7f; _p.grainFineLayer = true;
            _p.wrinkleAmount = 0.7f; _p.wrinklePeriod = 5; _p.wrinkleSharpness = 4f; _p.wrinkleAspect = 1.8f;
            _p.waveAmount = 0.35f; _p.microAmount = 0.1f; _p.normalStrength = 2f;
        }

        void RebuildPreview()
        {
            _dirty = false;
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }

            float[] H = LeatherNormalCore.BuildHeight(kPreviewRes, kPreviewRes, _p);
            Color[] px;
            switch (_previewMode)
            {
                case PreviewMode.ハイト:
                    px = LeatherNormalCore.BuildGray(H);
                    break;
                case PreviewMode.ライティング:
                    px = LeatherNormalCore.BuildLit(
                        LeatherNormalCore.HeightToNormal(H, kPreviewRes, kPreviewRes, _p.normalStrength, _p.flipGreen),
                        _lightAngle, 40f);
                    break;
                default:
                    px = LeatherNormalCore.HeightToNormal(H, kPreviewRes, kPreviewRes, _p.normalStrength, _p.flipGreen);
                    break;
            }
            _previewTex = new Texture2D(kPreviewRes, kPreviewRes, TextureFormat.RGBA32, false, true);
            _previewTex.SetPixels(px);
            _previewTex.Apply();
            _previewTex.filterMode = FilterMode.Bilinear;
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("プリセット", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("シボ強め")) { PresetShrink(); _dirty = true; GUI.FocusControl(null); }
                if (GUILayout.Button("スムース")) { PresetSmooth(); _dirty = true; GUI.FocusControl(null); }
                if (GUILayout.Button("ヴィンテージ")) { PresetVintage(); _dirty = true; GUI.FocusControl(null); }
            }

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("基本", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _p.seed = EditorGUILayout.IntField(new GUIContent("シード", "同じ値なら同じ模様を再現"), _p.seed);
                if (GUILayout.Button("乱数", GUILayout.Width(44)))
                {
                    _p.seed = Random.Range(1, 999999);
                    GUI.FocusControl(null);
                }
            }
            _resolution = (Resolution)EditorGUILayout.EnumPopup(new GUIContent("出力解像度"), _resolution);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("シボ（革の粒状の凹凸）", EditorStyles.boldLabel);
            _p.grainCells = EditorGUILayout.IntSlider(new GUIContent("細かさ", "タイル1辺あたりのセル数。大きいほど粒が細かい"), _p.grainCells, 8, 200);
            _p.grainDome = EditorGUILayout.Slider(new GUIContent("盛り上がり", "セル内の膨らみ量"), _p.grainDome, 0f, 1f);
            _p.creaseDepth = EditorGUILayout.Slider(new GUIContent("溝の深さ", "セル境界の溝の深さ"), _p.creaseDepth, 0f, 1f);
            _p.creaseWidth = EditorGUILayout.Slider(new GUIContent("溝の幅"), _p.creaseWidth, 0.02f, 0.5f);
            _p.grainVariation = EditorGUILayout.Slider(new GUIContent("高さばらつき", "セルごとのランダムな高低差"), _p.grainVariation, 0f, 1f);
            _p.grainJitter = EditorGUILayout.Slider(new GUIContent("配置ランダム", "0=整列 / 1=完全ランダム"), _p.grainJitter, 0f, 1f);
            _p.grainFineLayer = EditorGUILayout.Toggle(new GUIContent("細かいシボを重ねる", "3倍の細かさのシボを弱く重ねて粒感を出す"), _p.grainFineLayer);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("シワ（折りジワ・着用ジワ）", EditorStyles.boldLabel);
            _p.wrinkleAmount = EditorGUILayout.Slider(new GUIContent("深さ", "0でシワなし"), _p.wrinkleAmount, 0f, 1f);
            _p.wrinklePeriod = EditorGUILayout.IntSlider(new GUIContent("細かさ", "タイルあたりのシワ周期"), _p.wrinklePeriod, 1, 24);
            _p.wrinkleSharpness = EditorGUILayout.Slider(new GUIContent("鋭さ", "大きいほど細く鋭い折り目に"), _p.wrinkleSharpness, 0.5f, 8f);
            _p.wrinkleAspect = EditorGUILayout.Slider(new GUIContent("異方性", "1=等方 / 大きいほど横方向に走るシワ"), _p.wrinkleAspect, 0.3f, 4f);
            _p.wrinkleOctaves = EditorGUILayout.IntSlider(new GUIContent("重ね数", "周波数を重ねる数。多いほど複雑"), _p.wrinkleOctaves, 1, 6);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("うねり / 微細", EditorStyles.boldLabel);
            _p.waveAmount = EditorGUILayout.Slider(new GUIContent("うねり量", "革表面の緩やかな起伏"), _p.waveAmount, 0f, 1f);
            _p.wavePeriod = EditorGUILayout.IntSlider(new GUIContent("うねり周期"), _p.wavePeriod, 1, 12);
            _p.microAmount = EditorGUILayout.Slider(new GUIContent("ざらつき量", "銀面の微細な凹凸"), _p.microAmount, 0f, 0.5f);
            _p.microPeriod = EditorGUILayout.IntSlider(new GUIContent("ざらつき周期"), _p.microPeriod, 32, 512);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ノーマル変換", EditorStyles.boldLabel);
            _p.normalStrength = EditorGUILayout.Slider(new GUIContent("凹凸の強さ"), _p.normalStrength, 0.1f, 5f);
            _p.flipGreen = EditorGUILayout.Toggle(new GUIContent("Green(Y)反転", "DirectX系規約の場合ON（Unity標準はOFF）"), _p.flipGreen);

            if (EditorGUI.EndChangeCheck()) _dirty = true;

            // ---- プレビュー ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _previewMode = (PreviewMode)EditorGUILayout.EnumPopup(new GUIContent("表示", "ライティング=光を当てた質感確認"), _previewMode);
            if (_previewMode == PreviewMode.ライティング)
                _lightAngle = EditorGUILayout.Slider(new GUIContent("ライト角度(°)"), _lightAngle, 0f, 360f);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            if (_dirty) RebuildPreview();

            if (_previewTex != null)
            {
                float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 30f, 340f);
                Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r, _previewTex, null, ScaleMode.ScaleToFit);
                EditorGUILayout.HelpBox($"プレビューは{kPreviewRes}pxで計算。保存は{(int)_resolution}pxで処理します（見た目は解像度補正で一致）。シームレスタイル対応。", MessageType.None);
            }

            // ---- 保存 ----
            EditorGUILayout.Space();
            _saveHeightToo = EditorGUILayout.ToggleLeft("ハイトマップも一緒に保存（_Height.png）", _saveHeightToo);
            _chooseLocation = EditorGUILayout.ToggleLeft("保存時に場所を選ぶ（OFFで前回パスに上書き）", _chooseLocation);

            string ovPath = EditorPrefs.GetString(kSavePathKey, "");
            bool hasOv = !_chooseLocation && !string.IsNullOrEmpty(ovPath) && System.IO.File.Exists(ovPath)
                         && ovPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase);
            string saveLabel = hasOv ? $"上書き保存 ({System.IO.Path.GetFileName(ovPath)})" : "ノーマルマップを保存";
            if (GUILayout.Button(saveLabel, GUILayout.Height(30)))
                Save();

            EditorGUILayout.EndScrollView();
        }

        void Save()
        {
            int res = (int)_resolution;
            string path = ResolveSavePath("ノーマルマップを保存", "Leather_Normal.png", "png");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                EditorUtility.DisplayProgressBar("Leather Normal Generator", "ハイトマップを生成中...", 0.2f);
                float[] H = LeatherNormalCore.BuildHeight(res, res, _p);

                EditorUtility.DisplayProgressBar("Leather Normal Generator", "ノーマルへ変換中...", 0.6f);
                Color[] normals = LeatherNormalCore.HeightToNormal(H, res, res, _p.normalStrength, _p.flipGreen);

                EditorUtility.DisplayProgressBar("Leather Normal Generator", "保存中...", 0.85f);
                if (LeatherNormalCore.SaveNormal(normals, res, res, path))
                {
                    if (_saveHeightToo)
                    {
                        string hPath = System.IO.Path.Combine(
                            System.IO.Path.GetDirectoryName(path),
                            System.IO.Path.GetFileNameWithoutExtension(path) + "_Height.png");
                        LeatherNormalCore.SaveHeight(H, res, res, hPath);
                    }
                    AssetDatabase.Refresh();
                    Debug.Log($"[LeatherNormalGenerator] 保存しました: {path} ({res}x{res})");
                    EditorUtility.RevealInFinder(path);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // 保存先ディレクトリの記憶（テクスチャツール共通キー）
        const string kLastSaveDirKey = "TextureTools.LastSaveDir";
        const string kSavePathKey = "LeatherNormal.LastSavePath";
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
        string ResolveSavePath(string title, string defName, string ext)
        {
            string path = null;
            if (!_chooseLocation)
            {
                path = EditorPrefs.GetString(kSavePathKey, "");
                if (!string.IsNullOrEmpty(path) && (!System.IO.File.Exists(path)
                    || !path.EndsWith("." + ext, System.StringComparison.OrdinalIgnoreCase))) path = null;
            }
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel(title, GetLastSaveDir(), defName, ext);
                if (string.IsNullOrEmpty(path)) return null;
            }
            SetLastSaveDir(path);
            EditorPrefs.SetString(kSavePathKey, path);
            return path;
        }
    }
}
