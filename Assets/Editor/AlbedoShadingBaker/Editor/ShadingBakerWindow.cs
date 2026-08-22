// AlbedoShadingBaker - ShadingBakerWindow.cs
// メニュー: Tools > Albedo Shading Baker
// メッシュから法線マップをベイクし、そこから陰影マスク(中間0.5のLambert)を生成する。
// 単一アルベドを複数メッシュが共有している場合は、複数メッシュを同一UV空間の1枚へまとめて焼き、
// 単一の法線/陰影テクスチャとして出力できる。
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AlbedoShadingBaker
{
    public class ShadingBakerWindow : EditorWindow
    {
        // ---- 入力ソース ----
        // GameObject(MeshFilter/SkinnedMeshRenderer) または Mesh を複数指定できる
        readonly List<Object> _sources = new List<Object>();
        Texture2D _albedo;          // 共有アルベド（アルベド×陰影モード / 収集の基準 / 保存名の基準）

        // ---- ベイク設定 ----
        int _resolutionIndex = 2;   // 1024
        static readonly int[] kResolutions = { 256, 512, 1024, 2048, 4096 };
        static readonly string[] kResolutionLabels = { "256", "512", "1024", "2048", "4096" };
        NormalSpace _normalSpace = NormalSpace.Object;
        bool _flipY = false;
        int _dilationSteps = 4;

        // ---- 陰影設定 ----
        float _yaw = 30f;
        float _pitch = 35f;
        float _contrast = 1f;
        float _ambient = 0f;
        ShadingOutputMode _shadingMode = ShadingOutputMode.Mask;
        bool _autoUpdate = true;

        // 陰影方式
        enum ShadingMethod { LambertLight, Cavity }
        ShadingMethod _method = ShadingMethod.LambertLight;

        // 凹凸/キャビティ(Cavity)設定
        int _convRadius = 5;
        int _convDirIndex = 1; // 16
        static readonly int[] kConvDirs = { 8, 16, 32 };
        static readonly string[] kConvDirLabels = { "8", "16", "32" };
        float _convGain = 1f;
        bool _convAutoNormalize = true;
        bool _convInvert = false;
        float _convMinValid = 0.25f; // 周囲ノーマル有効率の下限（未満はグレー）

        // ---- 状態 ----
        RenderTexture _normalRT;
        RenderTexture _shadingRT;
        string _lastBakedName;
        Vector2 _scroll;

        [MenuItem("Tools/Albedo Shading Baker")]
        public static void Open()
        {
            var win = GetWindow<ShadingBakerWindow>("Shading Baker");
            win.minSize = new Vector2(360, 600);
            win.Show();
        }

        void OnEnable() { Persist(true); }

        void OnDisable()
        {
            Persist(false);
            ReleaseRT(ref _normalRT);
            ReleaseRT(ref _shadingRT);
        }

        // ---- パラメータの保持（EditorPrefs） ----
        static float Pf(string k, float v, bool load) { if (load) return EditorPrefs.GetFloat(k, v); EditorPrefs.SetFloat(k, v); return v; }
        static int Pi(string k, int v, bool load) { if (load) return EditorPrefs.GetInt(k, v); EditorPrefs.SetInt(k, v); return v; }
        static bool Pb(string k, bool v, bool load) { if (load) return EditorPrefs.GetBool(k, v); EditorPrefs.SetBool(k, v); return v; }

        void Persist(bool load)
        {
            const string P = "ShadingBaker.";
            _resolutionIndex = Pi(P + "res", _resolutionIndex, load);
            _normalSpace = (NormalSpace)Pi(P + "nspace", (int)_normalSpace, load);
            _flipY = Pb(P + "flipY", _flipY, load);
            _dilationSteps = Pi(P + "dilation", _dilationSteps, load);
            _yaw = Pf(P + "yaw", _yaw, load);
            _pitch = Pf(P + "pitch", _pitch, load);
            _contrast = Pf(P + "contrast", _contrast, load);
            _ambient = Pf(P + "ambient", _ambient, load);
            _shadingMode = (ShadingOutputMode)Pi(P + "smode", (int)_shadingMode, load);
            _autoUpdate = Pb(P + "auto", _autoUpdate, load);
            _method = (ShadingMethod)Pi(P + "method", (int)_method, load);
            _convRadius = Pi(P + "cRadius", _convRadius, load);
            _convDirIndex = Pi(P + "cDir", _convDirIndex, load);
            _convGain = Pf(P + "cGain", _convGain, load);
            _convAutoNormalize = Pb(P + "cAuto", _convAutoNormalize, load);
            _convInvert = Pb(P + "cInv", _convInvert, load);
            _convMinValid = Pf(P + "cMinValid", _convMinValid, load);
            _chooseLocation = Pb(P + "chooseLoc", _chooseLocation, load);
        }

        static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt != null)
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                rt = null;
            }
        }

        // ソース1件を Mesh + World行列 に解決
        static bool ResolveOne(Object src, out Mesh mesh, out Matrix4x4 matrix)
        {
            mesh = null;
            matrix = Matrix4x4.identity;
            if (src == null) return false;

            if (src is Mesh m)
            {
                mesh = m;
                return true;
            }
            if (src is GameObject go)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) mesh = mf.sharedMesh;
                else
                {
                    var smr = go.GetComponent<SkinnedMeshRenderer>();
                    if (smr != null && smr.sharedMesh != null) mesh = smr.sharedMesh;
                }
                matrix = go.transform.localToWorldMatrix;
                return mesh != null;
            }
            return false;
        }

        void ResolveMeshes(out List<Mesh> meshes, out List<Matrix4x4> matrices)
        {
            meshes = new List<Mesh>();
            matrices = new List<Matrix4x4>();
            foreach (var s in _sources)
            {
                if (ResolveOne(s, out Mesh mesh, out Matrix4x4 mat))
                {
                    meshes.Add(mesh);
                    matrices.Add(mat);
                }
            }
        }

        static bool MaterialReferences(Material mat, Texture tex)
        {
            if (mat == null || tex == null) return false;
            if (mat.mainTexture == tex) return true;
            string[] props = { "_MainTex", "_BaseMap", "_BaseColorMap", "_Albedo" };
            foreach (var p in props)
                if (mat.HasProperty(p) && mat.GetTexture(p) == tex) return true;
            return false;
        }

        void CollectByAlbedo()
        {
            if (_albedo == null) return;
            var renderers = Object.FindObjectsOfType<Renderer>();
            int added = 0;
            foreach (var r in renderers)
            {
                bool hit = false;
                foreach (var mat in r.sharedMaterials)
                    if (MaterialReferences(mat, _albedo)) { hit = true; break; }
                if (hit && !_sources.Contains(r.gameObject))
                {
                    _sources.Add(r.gameObject);
                    added++;
                }
            }
            Debug.Log($"[AlbedoShadingBaker] アルベド '{_albedo.name}' を参照するメッシュを {added} 件追加しました。");
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("共有アルベド", EditorStyles.boldLabel);
            _albedo = (Texture2D)EditorGUILayout.ObjectField(
                new GUIContent("アルベド", "アルベド×陰影モード・自動収集・保存名の基準に使用"),
                _albedo, typeof(Texture2D), false);
            using (new EditorGUI.DisabledScope(_albedo == null))
            {
                if (GUILayout.Button("このアルベドを参照するシーン内メッシュを収集"))
                    CollectByAlbedo();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("メッシュ（複数可・同一UV前提）", EditorStyles.boldLabel);
            DrawSourceList();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("法線ベイク設定", EditorStyles.boldLabel);
            _resolutionIndex = EditorGUILayout.Popup("解像度", _resolutionIndex, kResolutionLabels);
            _normalSpace = (NormalSpace)EditorGUILayout.EnumPopup(
                new GUIContent("法線空間", "Object=配置に依存しない / World=シーン上の向きを反映"), _normalSpace);
            _dilationSteps = EditorGUILayout.IntSlider(
                new GUIContent("縁の埋め(px)", "UVアイランド外周を埋めてシームを軽減"), _dilationSteps, 0, 16);
            _flipY = EditorGUILayout.Toggle(new GUIContent("Y反転", "出力が上下反転する場合にON"), _flipY);

            ResolveMeshes(out var meshes, out _);
            int validCount = 0;
            foreach (var mm in meshes)
                if (mm != null && mm.uv != null && mm.uv.Length > 0) validCount++;

            if (meshes.Count == 0)
                EditorGUILayout.HelpBox("メッシュを1つ以上指定してください（GameObject か Mesh をドロップ）。", MessageType.Info);
            else if (validCount == 0)
                EditorGUILayout.HelpBox("指定メッシュに UV がありません。ベイクにはUV展開が必要です。", MessageType.Warning);
            else if (validCount < meshes.Count)
                EditorGUILayout.HelpBox($"{meshes.Count}件中 {validCount}件が有効(UVあり)です。UVの無いメッシュはスキップされます。", MessageType.Info);
            else
                EditorGUILayout.HelpBox($"{validCount}件のメッシュを1枚にまとめてベイクします。", MessageType.None);

            using (new EditorGUI.DisabledScope(validCount == 0))
            {
                if (GUILayout.Button("① 法線マップをベイク（まとめて1枚）", GUILayout.Height(28)))
                    BakeNormal();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("陰影(中間0.5)", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var newMethod = (ShadingMethod)EditorGUILayout.EnumPopup(
                new GUIContent("方式", "LambertLight=ライト方向による陰影 / Cavity=凹凸(影の出やすさ)による陰影"), _method);
            bool methodChanged = newMethod != _method;
            _method = newMethod;

            bool shadingParamsChanged;
            if (_method == ShadingMethod.LambertLight)
            {
                _yaw = EditorGUILayout.Slider("ライト方位(Yaw)", _yaw, -180f, 180f);
                _pitch = EditorGUILayout.Slider("ライト仰角(Pitch)", _pitch, -90f, 90f);
                _contrast = EditorGUILayout.Slider("コントラスト", _contrast, 0f, 3f);
                _ambient = EditorGUILayout.Slider("アンビエント(下限持ち上げ)", _ambient, 0f, 1f);
                _shadingMode = (ShadingOutputMode)EditorGUILayout.EnumPopup("出力モード", _shadingMode);
                shadingParamsChanged = EditorGUI.EndChangeCheck();
            }
            else
            {
                _convRadius = EditorGUILayout.IntSlider(
                    new GUIContent("探索半径(px)", "この距離まで周囲の壁を探索して窪みを判定。大きいほど広い窪みを面で暗く"), _convRadius, 1, 32);
                _convDirIndex = EditorGUILayout.Popup(new GUIContent("探索方向数"), _convDirIndex, kConvDirLabels);
                _convGain = EditorGUILayout.Slider(new GUIContent("強さ(コントラスト)"), _convGain, 0f, 4f);
                _convAutoNormalize = EditorGUILayout.Toggle(
                    new GUIContent("自動正規化", "テクスチャ全体の分布に合わせ白黒レンジを自動調整"), _convAutoNormalize);
                _convInvert = EditorGUILayout.Toggle(new GUIContent("白黒反転"), _convInvert);
                _convMinValid = EditorGUILayout.Slider(
                    new GUIContent("有効サンプル下限", "周囲のノーマル有効率がこれ未満の(信頼できない)ピクセルはグレー(0.5)にする"),
                    _convMinValid, 0f, 1f);
                shadingParamsChanged = EditorGUI.EndChangeCheck();
                EditorGUILayout.HelpBox("影が出やすい(谷/凹＝壁に囲まれた窪み)=黒 / 出づらい(凸/開けている)=白 / 平坦=グレー。窪みは領域全体が暗くなる。基準グレーはテクスチャ全体平均。周囲のノーマルが少ない不正ピクセルはグレー。", MessageType.None);
            }

            _autoUpdate = EditorGUILayout.Toggle(
                new GUIContent("自動更新", "Lambertのみ即時更新。Convexityは負荷が高いため②ボタンで更新"), _autoUpdate);

            if (_normalRT == null)
                EditorGUILayout.HelpBox("先に「① 法線マップをベイク」を実行してください。", MessageType.Info);
            else if (methodChanged)
                UpdateShading(); // 方式切替時は一度更新
            else if (_method == ShadingMethod.LambertLight && _autoUpdate && shadingParamsChanged)
                UpdateShading();

            using (new EditorGUI.DisabledScope(_normalRT == null))
            {
                if (GUILayout.Button("② 陰影を更新", GUILayout.Height(24)))
                    UpdateShading();
            }

            // ---- プレビュー ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 30f, 260f);

            if (_normalRT != null)
            {
                EditorGUILayout.LabelField("法線マップ");
                Rect r1 = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r1, _normalRT);
            }
            if (_shadingRT != null)
            {
                EditorGUILayout.LabelField("陰影テクスチャ");
                Rect r2 = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r2, _shadingRT);
            }

            // ---- 保存 ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("PNG保存", EditorStyles.boldLabel);
            _chooseLocation = EditorGUILayout.ToggleLeft("保存時に場所を選ぶ（OFFで前回パスに上書き）", _chooseLocation);
            using (new EditorGUI.DisabledScope(_normalRT == null))
            {
                if (GUILayout.Button(SaveButtonLabel("NormalMap", "法線マップを保存")))
                    SavePng(_normalRT, "NormalMap", true);
            }
            using (new EditorGUI.DisabledScope(_shadingRT == null))
            {
                if (GUILayout.Button(SaveButtonLabel("Shading", "陰影テクスチャを保存")))
                    SavePng(_shadingRT, "Shading", false);
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawSourceList()
        {
            int removeIndex = -1;
            for (int i = 0; i < _sources.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _sources[i] = EditorGUILayout.ObjectField(_sources[i], typeof(Object), true);
                    if (GUILayout.Button("－", GUILayout.Width(24)))
                        removeIndex = i;
                }
            }
            if (removeIndex >= 0)
                _sources.RemoveAt(removeIndex);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 空きスロットを追加"))
                    _sources.Add(null);
                if (GUILayout.Button("選択中を追加"))
                {
                    foreach (var o in Selection.objects)
                        if ((o is GameObject || o is Mesh) && !_sources.Contains(o))
                            _sources.Add(o);
                }
                if (GUILayout.Button("クリア"))
                    _sources.Clear();
            }
        }

        void BakeNormal()
        {
            ResolveMeshes(out var meshes, out var matrices);
            if (meshes.Count == 0) return;

            ReleaseRT(ref _normalRT);
            int res = kResolutions[_resolutionIndex];
            _normalRT = ShadingBakerCore.BakeNormalMap(
                meshes, matrices, res, _normalSpace, _flipY, _dilationSteps);
            _lastBakedName = meshes.Count > 0 && meshes[0] != null ? meshes[0].name : null;

            if (_normalRT != null)
                UpdateShading();

            Repaint();
        }

        void EnsureShadingRT()
        {
            int res = _normalRT.width;
            if (_shadingRT == null || _shadingRT.width != res)
            {
                ReleaseRT(ref _shadingRT);
                _shadingRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "ShadingMask",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _shadingRT.Create();
            }
        }

        void UpdateShading()
        {
            if (_normalRT == null) return;
            EnsureShadingRT();

            if (_method == ShadingMethod.LambertLight)
            {
                Vector3 dir = ShadingBakerCore.DirectionFromYawPitch(_yaw, _pitch);
                ShadingBakerCore.GenerateShading(
                    _normalRT, _shadingRT, dir, _contrast, _ambient, _shadingMode, _albedo);
            }
            else
            {
                try
                {
                    EditorUtility.DisplayProgressBar("Albedo Shading Baker", "キャビティ(影の出やすさ)を計算中...", 0.4f);
                    int dirs = kConvDirs[_convDirIndex];
                    Color[] px = ShadingBakerCore.GenerateCavity(
                        _normalRT, _convRadius, dirs, _convGain, _convAutoNormalize, _convInvert,
                        _convMinValid, out int w, out int h);

                    var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, /*linear*/ false);
                    tmp.SetPixels(px);
                    tmp.Apply();
                    Graphics.Blit(tmp, _shadingRT);
                    Object.DestroyImmediate(tmp);
                }
                finally { EditorUtility.ClearProgressBar(); }
            }
            Repaint();
        }

        // 保存先ディレクトリの記憶（3ツール共通キー）
        const string kLastSaveDirKey = "TextureTools.LastSaveDir";
        bool _chooseLocation = false;
        static string SavePathKey(string suffix) => "ShadingBaker.LastSavePath." + suffix;
        string SaveButtonLabel(string suffix, string fallback)
        {
            string p = EditorPrefs.GetString(SavePathKey(suffix), "");
            if (!_chooseLocation && !string.IsNullOrEmpty(p) && System.IO.File.Exists(p))
                return $"上書き保存 ({System.IO.Path.GetFileName(p)})";
            return fallback;
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

        void SavePng(RenderTexture rt, string suffix, bool linear)
        {
            if (rt == null) return;

            // 保存名はアルベドテクスチャ名を優先（無ければ最初のメッシュ名 → Texture）
            string baseName = (_albedo != null) ? _albedo.name
                            : (!string.IsNullOrEmpty(_lastBakedName) ? _lastBakedName : "Texture");
            string defaultName = baseName + "_" + suffix + ".png";

            string key = SavePathKey(suffix);
            string path = null;
            if (!_chooseLocation)
            {
                path = EditorPrefs.GetString(key, "");
                if (!string.IsNullOrEmpty(path) && !System.IO.File.Exists(path)) path = null;
            }
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanel("PNGを保存", GetLastSaveDir(), defaultName, "png");
                if (string.IsNullOrEmpty(path)) return;
            }
            SetLastSaveDir(path);
            EditorPrefs.SetString(key, path);

            if (ShadingBakerCore.SaveRenderTextureToPng(rt, path, linear))
            {
                AssetDatabase.Refresh();
                Debug.Log($"[AlbedoShadingBaker] 保存しました: {path}");
                EditorUtility.RevealInFinder(path);
            }
        }
    }
}
