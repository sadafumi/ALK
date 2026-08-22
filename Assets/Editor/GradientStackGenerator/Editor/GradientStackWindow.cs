// GradientStackGenerator - GradientStackWindow.cs
// メニュー: Tools > Gradient Stack Generator
// 複数(既定6個)のGradientを縦に積み重ねて1枚のテクスチャにする。
// 各Gradientは横方向のランプになり、上から順に帯(バンド)として縦へ並ぶ。
// 各帯には「合成モード(6種)」をプルダウンで指定でき、その番号を Alpha チャンネルに埋め込む。
// 実際の合成計算は HLSL 側で行う（同梱の GradientStackBlend.hlsl を参照）。ツールはモード指定のみを書き出す。
using UnityEditor;
using UnityEngine;

namespace GradientStackGenerator
{
    // 合成モード（3種は確定、残り3種はあとで実装）
    public enum GsBlendMode { Add, Multiply, Overlay, Reserved4, Reserved5, Reserved6 }

    // Alphaチャンネルに何を書き込むか
    public enum GsAlphaContent { BlendMode, GradientAlpha, Opaque }

    // テクスチャのメタデータに保存する編集プリセット
    [System.Serializable]
    public class GsPreset
    {
        public int version = 1;
        public int count;
        public int width;
        public int bandHeight;
        public bool sRGB;
        public int alphaContent;
        public int[] blendModes;
        public Gradient[] gradients;
    }

    public class GradientStackWindow : EditorWindow
    {
        const string kPresetKey = "GradientStackPreset";
        const int kMax = 8;
        const int kBlendCount = 6;
        static readonly string[] kBlendLabels =
        {
            "加算 (Add)", "乗算 (Multiply)", "オーバーレイ (Overlay)", "(予約4)", "(予約5)", "(予約6)"
        };

        [SerializeField] Gradient[] _gradients = new Gradient[kMax];
        [SerializeField] int[] _blendModes = new int[kMax];
        [SerializeField] int _count = 6;
        [SerializeField] int _width = 256;
        [SerializeField] int _bandHeight = 16;
        [SerializeField] bool _sRGB = true;
        [SerializeField] GsAlphaContent _alphaContent = GsAlphaContent.BlendMode;

        Texture2D _previewTex;
        Texture2D _loadTex; // 編集用に読み込むテクスチャ
        Vector2 _scroll;
        bool _dirty = true;

        [MenuItem("Tools/Gradient Stack Generator")]
        public static void Open()
        {
            var win = GetWindow<GradientStackWindow>("Gradient Stack");
            win.minSize = new Vector2(380, 560);
            win.Show();
        }

        void OnEnable()
        {
            for (int i = 0; i < kMax; i++)
                if (_gradients[i] == null) _gradients[i] = DefaultGradient();
            Persist(true);
            _count = 6; // 本数は6固定
            _dirty = true;
        }

        void OnDisable()
        {
            Persist(false);
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }
        }

        static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        // 合成モード番号を Alpha 値へエンコード（HLSLは floor(a*6) で復号）
        static float EncodeMode(int mode)
        {
            mode = Mathf.Clamp(mode, 0, kBlendCount - 1);
            return (mode + 0.5f) / kBlendCount;
        }

        Color[] BuildPixels(int w, int bandH, int count)
        {
            int totalH = bandH * count;
            var px = new Color[w * totalH];
            for (int b = 0; b < count; b++)
            {
                var g = _gradients[b];
                float modeAlpha = EncodeMode(_blendModes[b]);
                int y0 = (count - 1 - b) * bandH; // band 0 を上に配置（Unityは下原点）
                for (int x = 0; x < w; x++)
                {
                    float t = (w <= 1) ? 0f : (float)x / (w - 1);
                    Color c = g.Evaluate(t);
                    switch (_alphaContent)
                    {
                        case GsAlphaContent.BlendMode: c.a = modeAlpha; break;
                        case GsAlphaContent.Opaque: c.a = 1f; break;
                        // GradientAlpha はそのまま
                    }
                    for (int yy = 0; yy < bandH; yy++)
                        px[(y0 + yy) * w + x] = c;
                }
            }
            return px;
        }

        void RebuildPreview()
        {
            _dirty = false;
            if (_previewTex != null) { Object.DestroyImmediate(_previewTex); _previewTex = null; }

            int w = Mathf.Clamp(_width, 1, 8192);
            int bandH = Mathf.Clamp(_bandHeight, 1, 1024);
            int count = Mathf.Clamp(_count, 1, kMax);
            var px = BuildPixels(w, bandH, count);
            _previewTex = new Texture2D(w, bandH * count, TextureFormat.RGBA32, false, !_sRGB);
            _previewTex.wrapMode = TextureWrapMode.Clamp;
            _previewTex.filterMode = FilterMode.Point; // モード番号がにじまないよう Point
            _previewTex.SetPixels(px);
            _previewTex.Apply();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("読み込み（編集を再開）", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _loadTex = (Texture2D)EditorGUILayout.ObjectField(
                    new GUIContent("生成テクスチャ", "このツールで作ったPNGを入れて設定を復元"), _loadTex, typeof(Texture2D), false);
                using (new EditorGUI.DisabledScope(_loadTex == null))
                    if (GUILayout.Button("設定を読み込む", GUILayout.Width(110)))
                        LoadFromTexture(_loadTex);
            }
            EditorGUILayout.HelpBox("このツールで生成したPNGを入れて「設定を読み込む」で、Gradientや設定を復元して編集を再開できます（メタデータから読み込み）。", MessageType.None);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gradient（上から順に縦へ並びます / 本数6固定）", EditorStyles.boldLabel);
            _count = 6;
            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < _count; i++)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    _gradients[i] = EditorGUILayout.GradientField(new GUIContent($"Gradient {i}"), _gradients[i]);
                    using (new EditorGUI.DisabledScope(_alphaContent != GsAlphaContent.BlendMode))
                        _blendModes[i] = EditorGUILayout.Popup(
                            new GUIContent("合成モード(Alphaへ)", "HLSL側で評価する合成方法。Alphaにモード番号を埋め込む"),
                            Mathf.Clamp(_blendModes[i], 0, kBlendCount - 1), kBlendLabels);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出力", EditorStyles.boldLabel);
            _alphaContent = (GsAlphaContent)EditorGUILayout.EnumPopup(
                new GUIContent("Alphaの内容",
                    "BlendMode=各帯の合成モード番号を埋め込む / GradientAlpha=GradientのAlpha / Opaque=不透明"),
                _alphaContent);
            _width = Mathf.Max(1, EditorGUILayout.IntField("幅", _width));
            _bandHeight = Mathf.Max(1, EditorGUILayout.IntField("1本あたりの高さ(px)", _bandHeight));
            _sRGB = EditorGUILayout.Toggle(new GUIContent("sRGB", "色ランプはON推奨 / データ用途はOFF(Linear)"), _sRGB);
            if (EditorGUI.EndChangeCheck()) _dirty = true;

            EditorGUILayout.LabelField("出力サイズ",
                $"{Mathf.Clamp(_width, 1, 8192)} x {Mathf.Clamp(_bandHeight, 1, 1024) * Mathf.Clamp(_count, 1, kMax)}");

            if (_alphaContent == GsAlphaContent.BlendMode)
                EditorGUILayout.HelpBox("Alphaに合成モード番号を埋め込みます。HLSLは floor(alpha*6) で復号（同梱 GradientStackBlend.hlsl）。インポートは sRGB OFF / Filter Point 推奨。", MessageType.None);

            if (_dirty) RebuildPreview();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プレビュー（RGB）", EditorStyles.boldLabel);
            if (_previewTex != null)
            {
                float w = EditorGUIUtility.currentViewWidth - 30f;
                float aspect = (float)_previewTex.height / Mathf.Max(1, _previewTex.width);
                float h = Mathf.Clamp(w * aspect, 40f, 320f);
                Rect r = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(true));
                EditorGUI.DrawPreviewTexture(r, _previewTex, null, ScaleMode.StretchToFill, 0f);
            }

            EditorGUILayout.Space();
            string overwriteTarget = _loadTex != null ? AssetDatabase.GetAssetPath(_loadTex) : EditorPrefs.GetString(kLastSavePathKey, "");
            bool canOverwrite = !string.IsNullOrEmpty(overwriteTarget) && System.IO.File.Exists(overwriteTarget);
            using (new EditorGUILayout.HorizontalScope())
            {
                string label = canOverwrite ? $"上書き保存 ({System.IO.Path.GetFileName(overwriteTarget)})" : "保存 (PNG)";
                if (GUILayout.Button(label, GUILayout.Height(28)))
                    Save(false);
                if (GUILayout.Button("別名で保存...", GUILayout.Width(110), GUILayout.Height(28)))
                    Save(true);
            }

            EditorGUILayout.EndScrollView();
        }

        void Save(bool forceDialog)
        {
            int w = Mathf.Clamp(_width, 1, 8192);
            int bandH = Mathf.Clamp(_bandHeight, 1, 1024);
            int count = Mathf.Clamp(_count, 1, kMax);

            // 保存先: 上書き（読み込み中テクスチャ→前回保存パス）／別名保存はダイアログ
            string path = null;
            if (!forceDialog)
            {
                if (_loadTex != null) path = AssetDatabase.GetAssetPath(_loadTex);
                if (string.IsNullOrEmpty(path)) path = EditorPrefs.GetString(kLastSavePathKey, "");
                if (!string.IsNullOrEmpty(path) && !System.IO.File.Exists(path)) path = null;
            }
            if (string.IsNullOrEmpty(path))
            {
                string baseName = _loadTex != null ? _loadTex.name : EditorPrefs.GetString(kLastFileNameKey, "GradientStack");
                if (string.IsNullOrEmpty(baseName)) baseName = "GradientStack";
                path = EditorUtility.SaveFilePanel("縦合成テクスチャを保存", GetLastSaveDir(), baseName + ".png", "png");
                if (string.IsNullOrEmpty(path)) return;
            }
            SetLastSaveDir(path);
            EditorPrefs.SetString(kLastFileNameKey, System.IO.Path.GetFileNameWithoutExtension(path));
            EditorPrefs.SetString(kLastSavePathKey, path); // 前回保存パスを記憶

            var px = BuildPixels(w, bandH, count);
            var tex = new Texture2D(w, bandH * count, TextureFormat.RGBA32, false, !_sRGB);
            tex.SetPixels(px);
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            // 編集プリセットをテクスチャのメタデータ(PNG tEXtチャンク)に埋め込む
            string presetJson = EditorJsonUtility.ToJson(BuildPreset());
            png = PngTextChunk.Inject(png, kPresetKey, presetJson);

            try
            {
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllBytes(path, png);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GradientStackGenerator] 保存に失敗: {e.Message}");
                return;
            }

            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            string normalized = path.Replace('\\', '/');
            string savedRelative = null;
            if (normalized.StartsWith(projectRoot + "/"))
            {
                savedRelative = normalized.Substring(projectRoot.Length + 1);
                AssetDatabase.ImportAsset(savedRelative, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(savedRelative) as TextureImporter;
                if (importer != null)
                {
                    // 合成モードを埋め込む場合は sRGB OFF / Point が安全
                    bool dataMode = _alphaContent == GsAlphaContent.BlendMode;
                    importer.sRGBTexture = dataMode ? false : _sRGB;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    if (dataMode) importer.filterMode = FilterMode.Point;
                    importer.userData = presetJson; // .meta へのバックアップ
                    importer.SaveAndReimport();
                }
            }
            AssetDatabase.Refresh();

            // 保存したテクスチャを「読み込み中テクスチャ」に切り替える（Assets内に保存した場合）
            if (!string.IsNullOrEmpty(savedRelative))
            {
                var savedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(savedRelative);
                if (savedTex != null) _loadTex = savedTex;
            }

            Debug.Log($"[GradientStackGenerator] 保存しました: {path} ({w}x{bandH * count})");
            EditorUtility.RevealInFinder(path);
            Repaint();
        }

        // ---- プリセット（テクスチャのメタデータ）----
        GsPreset BuildPreset()
        {
            var p = new GsPreset
            {
                count = _count,
                width = _width,
                bandHeight = _bandHeight,
                sRGB = _sRGB,
                alphaContent = (int)_alphaContent,
                blendModes = new int[kMax],
                gradients = new Gradient[kMax],
            };
            for (int i = 0; i < kMax; i++)
            {
                p.blendModes[i] = _blendModes[i];
                p.gradients[i] = _gradients[i];
            }
            return p;
        }

        void ApplyPreset(GsPreset p)
        {
            if (p == null) return;
            _count = 6; // 本数は6固定
            if (p.width > 0) _width = p.width;
            if (p.bandHeight > 0) _bandHeight = p.bandHeight;
            _sRGB = p.sRGB;
            _alphaContent = (GsAlphaContent)Mathf.Clamp(p.alphaContent, 0, 2);
            if (p.blendModes != null)
                for (int i = 0; i < kMax && i < p.blendModes.Length; i++) _blendModes[i] = p.blendModes[i];
            if (p.gradients != null)
                for (int i = 0; i < kMax && i < p.gradients.Length; i++)
                    if (p.gradients[i] != null) _gradients[i] = p.gradients[i];
            _dirty = true;
        }

        void LoadFromTexture(Texture2D t)
        {
            if (t == null) return;
            string path = AssetDatabase.GetAssetPath(t);
            string json = null;

            // [1] PNG の tEXt チャンク（テクスチャファイル自体のメタデータ）
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                try
                {
                    byte[] bytes = System.IO.File.ReadAllBytes(path);
                    json = PngTextChunk.Extract(bytes, kPresetKey);
                }
                catch { /* ignore */ }
            }
            // [2] フォールバック: .meta の userData
            if (string.IsNullOrEmpty(json) && !string.IsNullOrEmpty(path))
            {
                var imp = AssetImporter.GetAtPath(path);
                if (imp != null && !string.IsNullOrEmpty(imp.userData)) json = imp.userData;
            }

            if (string.IsNullOrEmpty(json))
            {
                EditorUtility.DisplayDialog("Gradient Stack",
                    "このテクスチャにプリセットデータが見つかりませんでした。\nこのツールで生成したPNGを指定してください。", "OK");
                return;
            }

            var p = new GsPreset();
            try { EditorJsonUtility.FromJsonOverwrite(json, p); }
            catch (System.Exception e) { Debug.LogError($"[GradientStackGenerator] 読み込み失敗: {e.Message}"); return; }
            ApplyPreset(p);
            Debug.Log($"[GradientStackGenerator] 設定を読み込みました: {path}");
        }

        // ---- パラメータ保持（EditorPrefs） ----
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
            const string P = "GradientStack.";
            _count = Pi(P + "count", _count, load);
            _width = Pi(P + "w", _width, load);
            _bandHeight = Pi(P + "bh", _bandHeight, load);
            _sRGB = Pb(P + "srgb", _sRGB, load);
            _alphaContent = (GsAlphaContent)Pi(P + "aContent", (int)_alphaContent, load);
            for (int i = 0; i < kMax; i++)
            {
                _blendModes[i] = Pi(P + "mode" + i, _blendModes[i], load);
                if (load) _gradients[i] = LoadGrad(P + "g" + i, _gradients[i]);
                else SaveGrad(P + "g" + i, _gradients[i]);
            }
        }

        // 保存先ディレクトリ・前回ファイル名の記憶
        const string kLastSaveDirKey = "TextureTools.LastSaveDir";
        const string kLastFileNameKey = "GradientStack.LastFileName";
        const string kLastSavePathKey = "GradientStack.LastSavePath";
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
    }
}
