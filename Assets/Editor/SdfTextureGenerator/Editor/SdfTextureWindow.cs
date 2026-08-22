// SdfTextureGenerator - SdfTextureWindow.cs
// メニュー: Tools > SDF Texture Generator
// 複数枚の白黒グラデーション（顔影などの各段階）を1枚の段階的SDFテクスチャへ合成して出力する。
// 参考: https://note.com/rid_316/n/ne967527e86ff
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SdfTextureGenerator
{
    public class SdfTextureWindow : EditorWindow
    {
        const int kPreviewMax = 512;

        readonly List<Texture2D> _stages = new List<Texture2D>();
        // 各段階のネイティブバッファ
        readonly List<Color[]> _bufs = new List<Color[]>();
        readonly List<int> _bw = new List<int>();
        readonly List<int> _bh = new List<int>();

        SourceChannel _srcChannel = SourceChannel.Luminance;
        bool _invertInput = false;

        // 入力方式
        enum InputMode { Stages, NormalSweep }
        InputMode _inputMode = InputMode.Stages;

        // ノーマルスイープ入力（ノーマル＋段階数から自動生成）
        Texture2D _normalTex;
        Color[] _normalBuf; int _nbW, _nbH;
        int _steps = 8;
        float _angleMin = -90f, _angleMax = 90f, _pitch = 0f;
        bool _greenFlip = false;
        // 中間テクスチャの抽出（明るい部分だけ取り出す）
        float _sweepThreshold = 0.5f;
        float _sweepFeather = 0.05f;
        bool _sweepBinarize = false;

        CombineMode _mode = CombineMode.Average;
        float _threshold = 0.5f;
        float _maxDist = 32f;

        OutputPrecision _precision = OutputPrecision.PackedRG16;

        enum ResMode { Auto, Manual }
        ResMode _resMode = ResMode.Auto;
        int _manualW = 1024, _manualH = 1024;

        Texture2D _previewTex;
        readonly List<Texture2D> _stageThumbs = new List<Texture2D>(); // 各角度の中間プレビュー
        Vector2 _scroll;
        bool _dirty;
        bool _chooseLocation = false;

        [MenuItem("Tools/SDF Texture Generator")]
        public static void Open()
        {
            var win = GetWindow<SdfTextureWindow>("SDF Generator");
            win.minSize = new Vector2(380, 660);
            win.Show();
        }

        void OnEnable() { Persist(true); }

        void OnDisable()
        {
            Persist(false);
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }
            ClearThumbs();
        }

        void ClearThumbs()
        {
            foreach (var t in _stageThumbs) if (t != null) Object.DestroyImmediate(t);
            _stageThumbs.Clear();
        }

        // ---- パラメータの保持（EditorPrefs） ----
        static float Pf(string k, float v, bool load) { if (load) return EditorPrefs.GetFloat(k, v); EditorPrefs.SetFloat(k, v); return v; }
        static int Pi(string k, int v, bool load) { if (load) return EditorPrefs.GetInt(k, v); EditorPrefs.SetInt(k, v); return v; }
        static bool Pb(string k, bool v, bool load) { if (load) return EditorPrefs.GetBool(k, v); EditorPrefs.SetBool(k, v); return v; }

        void Persist(bool load)
        {
            const string P = "SdfGenerator.";
            _inputMode = (InputMode)Pi(P + "inMode", (int)_inputMode, load);
            _steps = Pi(P + "steps", _steps, load);
            _angleMin = Pf(P + "aMin", _angleMin, load);
            _angleMax = Pf(P + "aMax", _angleMax, load);
            _pitch = Pf(P + "pitch", _pitch, load);
            _greenFlip = Pb(P + "gflip", _greenFlip, load);
            _sweepThreshold = Pf(P + "swTh", _sweepThreshold, load);
            _sweepFeather = Pf(P + "swFe", _sweepFeather, load);
            _sweepBinarize = Pb(P + "swBin", _sweepBinarize, load);
            _srcChannel = (SourceChannel)Pi(P + "ch", (int)_srcChannel, load);
            _invertInput = Pb(P + "inv", _invertInput, load);
            _mode = (CombineMode)Pi(P + "mode", (int)_mode, load);
            _threshold = Pf(P + "th", _threshold, load);
            _maxDist = Pf(P + "maxD", _maxDist, load);
            _precision = (OutputPrecision)Pi(P + "prec", (int)_precision, load);
            _resMode = (ResMode)Pi(P + "resMode", (int)_resMode, load);
            _manualW = Pi(P + "mw", _manualW, load);
            _manualH = Pi(P + "mh", _manualH, load);
            _chooseLocation = Pb(P + "chooseLoc", _chooseLocation, load);
        }

        void ReloadStages()
        {
            _bufs.Clear(); _bw.Clear(); _bh.Clear();
            foreach (var t in _stages)
            {
                if (t == null) { _bufs.Add(null); _bw.Add(0); _bh.Add(0); continue; }
                Color[] px = SdfTextureCore.ReadTexture(t, out int w, out int h);
                _bufs.Add(px); _bw.Add(w); _bh.Add(h);
            }
            _dirty = true;
        }

        void LoadNormal()
        {
            _normalBuf = (_normalTex != null) ? SdfTextureCore.ReadTexture(_normalTex, out _nbW, out _nbH) : null;
            _dirty = true;
        }

        int StageCount()
        {
            int c = 0;
            foreach (var b in _bufs) if (b != null) c++;
            return c;
        }

        bool HasInput()
        {
            return _inputMode == InputMode.NormalSweep ? (_normalBuf != null) : (StageCount() > 0);
        }

        void GetOutputSize(out int w, out int h)
        {
            if (_resMode == ResMode.Manual)
            {
                w = Mathf.Clamp(_manualW, 1, 8192);
                h = Mathf.Clamp(_manualH, 1, 8192);
                return;
            }
            w = 0; h = 0;
            if (_inputMode == InputMode.NormalSweep)
            {
                if (_normalBuf != null) { w = _nbW; h = _nbH; }
            }
            else
            {
                for (int i = 0; i < _bufs.Count; i++)
                {
                    if (_bufs[i] == null) continue;
                    w = Mathf.Max(w, _bw[i]);
                    h = Mathf.Max(h, _bh[i]);
                }
            }
            if (w == 0) { w = 256; h = 256; }
        }

        float[] ComputeV(int tw, int th, float maxDistPx)
        {
            List<float[]> grays;
            if (_inputMode == InputMode.NormalSweep)
            {
                if (_normalBuf == null) return new float[tw * th];
                grays = SdfTextureCore.BuildSweepStages(_normalBuf, _nbW, _nbH, tw, th,
                    _steps, _angleMin, _angleMax, _pitch, _greenFlip,
                    _sweepThreshold, _sweepFeather, _sweepBinarize);
            }
            else
            {
                grays = new List<float[]>();
                for (int i = 0; i < _bufs.Count; i++)
                {
                    if (_bufs[i] == null) continue;
                    grays.Add(SdfTextureCore.ResampleGray(_bufs[i], _bw[i], _bh[i], tw, th, _srcChannel, _invertInput));
                }
            }
            return SdfTextureCore.Combine(grays, tw, th, _mode, _threshold, maxDistPx);
        }

        void RebuildPreview()
        {
            _dirty = false;
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }
            ClearThumbs();
            if (!HasInput()) return;

            GetOutputSize(out int ow, out int oh);
            int longest = Mathf.Max(ow, oh);
            float scale = longest > kPreviewMax ? (float)kPreviewMax / longest : 1f;
            int pw = Mathf.Max(1, Mathf.RoundToInt(ow * scale));
            int ph = Mathf.Max(1, Mathf.RoundToInt(oh * scale));

            float[] V = ComputeV(pw, ph, _maxDist * scale); // 距離は解像度比でスケール
            var px = SdfTextureCore.BuildGray8(V);
            _previewTex = new Texture2D(pw, ph, TextureFormat.RGBA32, false, true);
            _previewTex.SetPixels(px);
            _previewTex.Apply();
            _previewTex.filterMode = FilterMode.Bilinear;

            BuildThumbs();
        }

        // 各角度の中間テクスチャ（光の当たり具合）をサムネイル生成
        void BuildThumbs()
        {
            ClearThumbs();
            if (_inputMode != InputMode.NormalSweep || _normalBuf == null) return;

            GetOutputSize(out int ow, out int oh);
            int longest = Mathf.Max(ow, oh);
            float scale = longest > 96 ? 96f / longest : 1f;
            int tw = Mathf.Max(1, Mathf.RoundToInt(ow * scale));
            int th = Mathf.Max(1, Mathf.RoundToInt(oh * scale));

            var stages = SdfTextureCore.BuildSweepStages(_normalBuf, _nbW, _nbH, tw, th,
                _steps, _angleMin, _angleMax, _pitch, _greenFlip,
                _sweepThreshold, _sweepFeather, _sweepBinarize);
            foreach (var g in stages)
            {
                var gc = SdfTextureCore.BuildGray8(g);
                var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false, true);
                tex.SetPixels(gc);
                tex.Apply();
                tex.filterMode = FilterMode.Bilinear;
                _stageThumbs.Add(tex);
            }
        }

        float AngleAt(int i)
        {
            float t = (_steps <= 1) ? 0.5f : (float)i / (_steps - 1);
            return Mathf.Lerp(_angleMin, _angleMax, t);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("入力方式", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _inputMode = (InputMode)EditorGUILayout.EnumPopup(
                new GUIContent("入力方式", "Stages=段階マスクを手動指定 / NormalSweep=ノーマル＋段階数から自動生成"), _inputMode);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            if (_inputMode == InputMode.Stages)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("段階テクスチャ（明→暗の順に並べる）", EditorStyles.boldLabel);
                DrawStageList();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("入力の読み取り", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                _srcChannel = (SourceChannel)EditorGUILayout.EnumPopup(new GUIContent("読取チャンネル"), _srcChannel);
                _invertInput = EditorGUILayout.Toggle(new GUIContent("入力を反転"), _invertInput);
                if (EditorGUI.EndChangeCheck()) _dirty = true;
            }
            else // NormalSweep
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("ノーマル＋段階数", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                _normalTex = (Texture2D)EditorGUILayout.ObjectField(
                    new GUIContent("ノーマルテクスチャ", "このノーマルにライトを段階的に当てて各段階を自動生成"),
                    _normalTex, typeof(Texture2D), false);
                if (EditorGUI.EndChangeCheck()) LoadNormal();

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("プリセット", GUILayout.Width(60));
                    if (GUILayout.Button("AnimeXD 9角度 (0°→180°)"))
                    {
                        _angleMin = 0f; _angleMax = 180f; _steps = 9;
                        _mode = CombineMode.ThresholdStack; _threshold = 0.5f;
                        _dirty = true; GUI.FocusControl(null);
                    }
                }

                EditorGUI.BeginChangeCheck();
                _steps = EditorGUILayout.IntSlider(new GUIContent("段階数", "ライトスイープの分割数（AnimeXDは9・減らしてもOK）"), _steps, 2, 64);
                _angleMin = EditorGUILayout.Slider(new GUIContent("スイープ開始角(°)"), _angleMin, -180f, 180f);
                _angleMax = EditorGUILayout.Slider(new GUIContent("スイープ終了角(°)"), _angleMax, -180f, 180f);
                _pitch = EditorGUILayout.Slider(new GUIContent("仰角(°)", "ライトの上下角"), _pitch, -89f, 89f);
                _greenFlip = EditorGUILayout.Toggle(new GUIContent("Green(Y)反転", "法線マップのYが逆の場合ON"), _greenFlip);
                EditorGUILayout.LabelField("中間テクスチャの抽出", EditorStyles.miniBoldLabel);
                _sweepThreshold = EditorGUILayout.Slider(new GUIContent("抽出しきい値", "この明るさ以上の部分だけを抽出"), _sweepThreshold, 0f, 1f);
                using (new EditorGUI.DisabledScope(_sweepBinarize))
                    _sweepFeather = EditorGUILayout.Slider(new GUIContent("エッジぼかし", "しきい値境界の柔らかさ（二値化OFF時）"), _sweepFeather, 0f, 0.5f);
                _sweepBinarize = EditorGUILayout.Toggle(new GUIContent("二値化", "ON=白黒でハード抽出 / OFF=輝度を保持して抽出"), _sweepBinarize);
                if (EditorGUI.EndChangeCheck()) _dirty = true;

                if (_normalTex == null)
                    EditorGUILayout.HelpBox("ノーマルテクスチャを指定してください。", MessageType.Info);
                else
                    EditorGUILayout.HelpBox("ノーマルにライトを開始角→終了角へ段階数ぶんスイープし、各角度の光の当たり具合を生成→SDFへ合成します。AnimeXD相当は合成モード ThresholdStack を推奨。", MessageType.None);

                using (new EditorGUI.DisabledScope(_normalBuf == null))
                {
                    if (GUILayout.Button("各角度の中間テクスチャを書き出す (a,b,c…)"))
                        ExportStages();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("合成", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _mode = (CombineMode)EditorGUILayout.EnumPopup(
                new GUIContent("合成モード",
                    "Average=各段階(グラデ)の平均 / ThresholdStack=しきい値以上の段階数 / DistanceBlend=距離変換ブレンド"),
                _mode);
            using (new EditorGUI.DisabledScope(_mode == CombineMode.Average))
            {
                _threshold = EditorGUILayout.Slider(new GUIContent("しきい値"), _threshold, 0f, 1f);
            }
            using (new EditorGUI.DisabledScope(_mode != CombineMode.DistanceBlend))
            {
                _maxDist = EditorGUILayout.Slider(
                    new GUIContent("距離レンジ(px)", "符号付き距離を±このpxで0..1へ正規化"), _maxDist, 1f, 256f);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出力", EditorStyles.boldLabel);
            _precision = (OutputPrecision)EditorGUILayout.EnumPopup(
                new GUIContent("精度/形式",
                    "Gray8=8bit PNG / PackedRG16=16bit相当をRGにパックPNG / ExrFloat=EXR浮動小数"),
                _precision);
            _resMode = (ResMode)EditorGUILayout.EnumPopup(new GUIContent("解像度モード"), _resMode);
            using (new EditorGUI.DisabledScope(_resMode != ResMode.Manual))
            {
                _manualW = EditorGUILayout.IntField("幅", _manualW);
                _manualH = EditorGUILayout.IntField("高さ", _manualH);
            }
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            GetOutputSize(out int ow, out int oh);
            int stageInfo = _inputMode == InputMode.NormalSweep ? (_normalBuf != null ? _steps : 0) : StageCount();
            EditorGUILayout.LabelField("出力サイズ", $"{ow} x {oh} / 段階数 {stageInfo}");

            if (_precision == OutputPrecision.PackedRG16)
                EditorGUILayout.HelpBox("シェーダ側は DecodeFloatRG(tex.rg) で復号（sRGBはOFF/Linearで読み込み）。", MessageType.None);

            if (_dirty) RebuildPreview();

            // ---- プレビュー ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プレビュー（SDF値）", EditorStyles.boldLabel);
            if (_previewTex != null)
            {
                float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 30f, 300f);
                Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r, _previewTex, null, ScaleMode.ScaleToFit);
                EditorGUILayout.HelpBox("プレビューは縮小・8bit表示です。保存は出力サイズ・指定精度で処理します。", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("段階テクスチャを追加、またはノーマルテクスチャを指定してください。", MessageType.Info);
            }

            // ---- 各角度の中間プレビュー ----
            if (_inputMode == InputMode.NormalSweep && _stageThumbs.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("各角度の光の当たり具合（中間）", EditorStyles.boldLabel);
                const int perRow = 5;
                for (int i = 0; i < _stageThumbs.Count; i += perRow)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        for (int j = i; j < Mathf.Min(i + perRow, _stageThumbs.Count); j++)
                        {
                            using (new EditorGUILayout.VerticalScope(GUILayout.Width(64)))
                            {
                                Rect tr = GUILayoutUtility.GetRect(60, 60, GUILayout.Width(60), GUILayout.Height(60));
                                EditorGUI.DrawPreviewTexture(tr, _stageThumbs[j], null, ScaleMode.ScaleToFit);
                                EditorGUILayout.LabelField($"{AngleAt(j):0.#}°", EditorStyles.miniLabel, GUILayout.Width(60));
                            }
                        }
                    }
                }
            }

            // ---- 保存 ----
            EditorGUILayout.Space();
            _chooseLocation = EditorGUILayout.ToggleLeft("保存時に場所を選ぶ（OFFで前回パスに上書き）", _chooseLocation);
            using (new EditorGUI.DisabledScope(!HasInput()))
            {
                string sext = _precision == OutputPrecision.ExrFloat ? "exr" : "png";
                string ovPath = EditorPrefs.GetString(kSavePathKey, "");
                bool hasOv = !_chooseLocation && !string.IsNullOrEmpty(ovPath) && System.IO.File.Exists(ovPath)
                             && ovPath.EndsWith("." + sext, System.StringComparison.OrdinalIgnoreCase);
                string saveLabel = hasOv ? $"上書き保存 ({System.IO.Path.GetFileName(ovPath)})" : "SDFテクスチャを保存";
                if (GUILayout.Button(saveLabel, GUILayout.Height(30)))
                    Save();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawStageList()
        {
            int remove = -1, up = -1, down = -1;
            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < _stages.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{i}", GUILayout.Width(20));
                    _stages[i] = (Texture2D)EditorGUILayout.ObjectField(_stages[i], typeof(Texture2D), false);
                    if (GUILayout.Button("▲", GUILayout.Width(24))) up = i;
                    if (GUILayout.Button("▼", GUILayout.Width(24))) down = i;
                    if (GUILayout.Button("－", GUILayout.Width(24))) remove = i;
                }
            }
            bool changed = EditorGUI.EndChangeCheck();

            if (remove >= 0) { _stages.RemoveAt(remove); changed = true; }
            if (up > 0) { var t = _stages[up]; _stages[up] = _stages[up - 1]; _stages[up - 1] = t; changed = true; }
            if (down >= 0 && down < _stages.Count - 1) { var t = _stages[down]; _stages[down] = _stages[down + 1]; _stages[down + 1] = t; changed = true; }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 追加")) { _stages.Add(null); changed = true; }
                if (GUILayout.Button("選択中を追加"))
                {
                    foreach (var o in Selection.objects)
                        if (o is Texture2D tx) _stages.Add(tx);
                    changed = true;
                }
                if (GUILayout.Button("クリア")) { _stages.Clear(); changed = true; }
            }

            if (changed) ReloadStages();
        }

        void ExportStages()
        {
            if (_normalBuf == null) return;
            GetOutputSize(out int ow, out int oh);
            string folder = EditorUtility.SaveFolderPanel("各角度テクスチャの保存先フォルダ", GetLastSaveDir(), "");
            if (string.IsNullOrEmpty(folder)) return;
            SetLastSaveDir(folder + "/dummy"); // フォルダをディレクトリとして記憶

            var stages = SdfTextureCore.BuildSweepStages(_normalBuf, _nbW, _nbH, ow, oh,
                _steps, _angleMin, _angleMax, _pitch, _greenFlip,
                _sweepThreshold, _sweepFeather, _sweepBinarize);
            try
            {
                for (int i = 0; i < stages.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("SDF Texture Generator",
                        $"中間テクスチャを書き出し中... {i + 1}/{stages.Count}", (float)i / stages.Count);
                    char letter = (char)('a' + Mathf.Min(i, 25));
                    string name = $"{letter}_{AngleAt(i):000}deg.png";
                    string path = System.IO.Path.Combine(folder, name);
                    SdfTextureCore.Save(stages[i], ow, oh, path, OutputPrecision.Gray8);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.Refresh();
            Debug.Log($"[SdfTextureGenerator] {stages.Count} 枚の中間テクスチャを書き出しました: {folder}");
            EditorUtility.RevealInFinder(folder);
        }

        void Save()
        {
            if (!HasInput()) return;
            GetOutputSize(out int ow, out int oh);

            string ext = _precision == OutputPrecision.ExrFloat ? "exr" : "png";
            string def = "FaceSDF_" + _precision + "." + ext;
            string path = ResolveSavePath("SDFテクスチャを保存", def, ext);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                EditorUtility.DisplayProgressBar("SDF Texture Generator", "SDFを生成中...", 0.4f);
                float[] V = ComputeV(ow, oh, _maxDist);
                if (SdfTextureCore.Save(V, ow, oh, path, _precision))
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[SdfTextureGenerator] 保存しました: {path} ({ow}x{oh}, {_precision})");
                    EditorUtility.RevealInFinder(path);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        // 保存先ディレクトリの記憶（4ツール共通キー）
        const string kLastSaveDirKey = "TextureTools.LastSaveDir";
        const string kSavePathKey = "SdfGenerator.LastSavePath";
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
