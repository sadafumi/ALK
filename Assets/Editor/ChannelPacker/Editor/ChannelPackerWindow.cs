// ChannelPacker - ChannelPackerWindow.cs
// メニュー: Tools > Channel Packer
// R/G/B/A に別々のグレースケールテクスチャを割り当て、1枚のテクスチャに合成して出力する。
using UnityEditor;
using UnityEngine;

namespace ChannelPacker
{
    public class ChannelPackerWindow : EditorWindow
    {
        const int kPreviewMax = 512;
        static readonly string[] kSlotNames = { "R チャンネル", "G チャンネル", "B チャンネル", "A チャンネル" };

        // スロット状態(0=R,1=G,2=B,3=A)
        readonly Texture2D[] _tex = new Texture2D[4];
        readonly SourceChannel[] _channel = { SourceChannel.R, SourceChannel.R, SourceChannel.R, SourceChannel.R };
        readonly bool[] _invert = new bool[4];
        readonly float[] _constant = { 0f, 0f, 0f, 1f };
        readonly SourceBuffer[] _buf = new SourceBuffer[4];

        enum ResMode { Auto, Manual }
        ResMode _resMode = ResMode.Auto;
        int _manualW = 1024;
        int _manualH = 1024;

        OutputColorSpace _colorSpace = OutputColorSpace.Linear;

        Texture2D _previewRGB;
        Texture2D _previewA;
        Vector2 _scroll;
        bool _dirty;

        [MenuItem("Tools/Channel Packer")]
        public static void Open()
        {
            var win = GetWindow<ChannelPackerWindow>("Channel Packer");
            win.minSize = new Vector2(380, 640);
            win.Show();
        }

        void OnEnable() { Persist(true); }

        // ---- パラメータの保持（EditorPrefs） ----
        static float Pf(string k, float v, bool load) { if (load) return EditorPrefs.GetFloat(k, v); EditorPrefs.SetFloat(k, v); return v; }
        static int Pi(string k, int v, bool load) { if (load) return EditorPrefs.GetInt(k, v); EditorPrefs.SetInt(k, v); return v; }
        static bool Pb(string k, bool v, bool load) { if (load) return EditorPrefs.GetBool(k, v); EditorPrefs.SetBool(k, v); return v; }

        void Persist(bool load)
        {
            const string P = "ChannelPacker.";
            for (int i = 0; i < 4; i++)
            {
                _channel[i] = (SourceChannel)Pi(P + "ch" + i, (int)_channel[i], load);
                _invert[i] = Pb(P + "inv" + i, _invert[i], load);
                _constant[i] = Pf(P + "const" + i, _constant[i], load);
            }
            _colorSpace = (OutputColorSpace)Pi(P + "cs", (int)_colorSpace, load);
            _resMode = (ResMode)Pi(P + "resMode", (int)_resMode, load);
            _manualW = Pi(P + "mw", _manualW, load);
            _manualH = Pi(P + "mh", _manualH, load);
            _chooseLocation = Pb(P + "chooseLoc", _chooseLocation, load);
        }

        void OnDisable()
        {
            Persist(false);
            DestroyTex(ref _previewRGB);
            DestroyTex(ref _previewA);
        }

        static void DestroyTex(ref Texture2D t)
        {
            if (t != null) { Object.DestroyImmediate(t); t = null; }
        }

        void ReloadBuffer(int i)
        {
            _buf[i] = (_tex[i] != null) ? ChannelPackerCore.ReadTexture(_tex[i]) : null;
        }

        bool HasAnySource()
        {
            for (int i = 0; i < 4; i++) if (_buf[i] != null) return true;
            return false;
        }

        void GetOutputSize(out int w, out int h)
        {
            if (_resMode == ResMode.Manual)
            {
                w = Mathf.Clamp(_manualW, 1, 8192);
                h = Mathf.Clamp(_manualH, 1, 8192);
                return;
            }
            // Auto: 割り当て済み入力の最大サイズ
            w = 0; h = 0;
            for (int i = 0; i < 4; i++)
            {
                if (_buf[i] == null) continue;
                w = Mathf.Max(w, _buf[i].width);
                h = Mathf.Max(h, _buf[i].height);
            }
            if (w == 0) { w = 256; h = 256; }
        }

        SlotInput[] BuildSlots()
        {
            var slots = new SlotInput[4];
            for (int i = 0; i < 4; i++)
            {
                slots[i] = new SlotInput
                {
                    buffer = _buf[i],
                    channel = _channel[i],
                    invert = _invert[i],
                    constant = _constant[i],
                };
            }
            return slots;
        }

        void RebuildPreview()
        {
            _dirty = false;
            GetOutputSize(out int ow, out int oh);

            // プレビュー解像度(アスペクト維持で最長辺512)
            int longest = Mathf.Max(ow, oh);
            float scale = longest > kPreviewMax ? (float)kPreviewMax / longest : 1f;
            int pw = Mathf.Max(1, Mathf.RoundToInt(ow * scale));
            int ph = Mathf.Max(1, Mathf.RoundToInt(oh * scale));

            var slots = BuildSlots();
            Color[] packed = ChannelPackerCore.Pack(slots, pw, ph);

            // RGBプレビュー
            var rgb = new Color[packed.Length];
            var alpha = new Color[packed.Length];
            for (int i = 0; i < packed.Length; i++)
            {
                rgb[i] = new Color(packed[i].r, packed[i].g, packed[i].b, 1f);
                float a = packed[i].a;
                alpha[i] = new Color(a, a, a, 1f);
            }

            DestroyTex(ref _previewRGB);
            DestroyTex(ref _previewA);
            _previewRGB = ChannelPackerCore.BuildTexture(rgb, pw, ph, false);
            _previewRGB.filterMode = FilterMode.Bilinear;
            _previewA = ChannelPackerCore.BuildTexture(alpha, pw, ph, false);
            _previewA.filterMode = FilterMode.Bilinear;
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("チャンネル入力", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < 4; i++)
                DrawSlot(i);
            bool slotChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("出力設定", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _colorSpace = (OutputColorSpace)EditorGUILayout.EnumPopup(
                new GUIContent("カラースペース", "Linear=マスク/データマップ向け(sRGB OFF) / sRGB=見た目重視"),
                _colorSpace);
            _resMode = (ResMode)EditorGUILayout.EnumPopup(
                new GUIContent("解像度モード", "Auto=入力の最大サイズ / Manual=手動指定"), _resMode);
            using (new EditorGUI.DisabledScope(_resMode != ResMode.Manual))
            {
                _manualW = EditorGUILayout.IntField("幅", _manualW);
                _manualH = EditorGUILayout.IntField("高さ", _manualH);
            }
            bool outChanged = EditorGUI.EndChangeCheck();

            GetOutputSize(out int ow, out int oh);
            EditorGUILayout.LabelField("出力サイズ", $"{ow} x {oh}");

            if (slotChanged || outChanged) _dirty = true;
            if (_dirty) RebuildPreview();

            // ---- プレビュー ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 30f, 240f);
            if (_previewRGB != null)
            {
                EditorGUILayout.LabelField("RGB合成");
                Rect r1 = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r1, _previewRGB, null, ScaleMode.ScaleToFit);
            }
            if (_previewA != null)
            {
                EditorGUILayout.LabelField("Alpha (グレー表示)");
                Rect r2 = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r2, _previewA, null, ScaleMode.ScaleToFit);
            }
            EditorGUILayout.HelpBox("プレビューは縮小表示です。保存は出力サイズで処理します。", MessageType.None);

            // ---- 保存 ----
            EditorGUILayout.Space();
            _chooseLocation = EditorGUILayout.ToggleLeft("保存時に場所を選ぶ（OFFで前回パスに上書き）", _chooseLocation);
            using (new EditorGUI.DisabledScope(!HasAnySource() && _resMode == ResMode.Auto))
            {
                string ovPath = EditorPrefs.GetString(kSavePathKey, "");
                bool hasOv = !_chooseLocation && !string.IsNullOrEmpty(ovPath) && System.IO.File.Exists(ovPath);
                string saveLabel = hasOv ? $"上書き保存 ({System.IO.Path.GetFileName(ovPath)})" : "パックしてPNG保存";
                if (GUILayout.Button(saveLabel, GUILayout.Height(30)))
                    SavePacked();
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawSlot(int i)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(kSlotNames[i], EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                _tex[i] = (Texture2D)EditorGUILayout.ObjectField(
                    new GUIContent("テクスチャ"), _tex[i], typeof(Texture2D), false);
                if (EditorGUI.EndChangeCheck())
                {
                    ReloadBuffer(i);
                    _dirty = true;
                }

                using (new EditorGUI.DisabledScope(_tex[i] == null))
                {
                    _channel[i] = (SourceChannel)EditorGUILayout.EnumPopup(
                        new GUIContent("読取チャンネル"), _channel[i]);
                }
                _invert[i] = EditorGUILayout.Toggle(new GUIContent("反転"), _invert[i]);

                using (new EditorGUI.DisabledScope(_tex[i] != null))
                {
                    _constant[i] = EditorGUILayout.Slider(
                        new GUIContent("定数値(テクスチャ未指定時)"), _constant[i], 0f, 1f);
                }
            }
        }

        // 保存先ディレクトリの記憶（3ツール共通キー）
        const string kLastSaveDirKey = "TextureTools.LastSaveDir";
        const string kSavePathKey = "ChannelPacker.LastSavePath";
        bool _chooseLocation = false;
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

        void SavePacked()
        {
            GetOutputSize(out int ow, out int oh);
            // 保存名は R チャンネルのテクスチャ名を基準にする（無ければ Packed）
            string baseName = (_tex[0] != null) ? _tex[0].name : "Packed";
            string def = baseName + "_RGBA.png";
            string path = ResolveSavePath("パックしたテクスチャを保存", def);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                EditorUtility.DisplayProgressBar("Channel Packer", "チャンネルを合成中...", 0.4f);
                var slots = BuildSlots();
                Color[] packed = ChannelPackerCore.Pack(slots, ow, oh);
                if (ChannelPackerCore.SavePng(packed, ow, oh, path, _colorSpace))
                {
                    AssetDatabase.Refresh();
                    Debug.Log($"[ChannelPacker] 保存しました: {path} ({ow}x{oh}, {_colorSpace})");
                    EditorUtility.RevealInFinder(path);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }
    }
}
