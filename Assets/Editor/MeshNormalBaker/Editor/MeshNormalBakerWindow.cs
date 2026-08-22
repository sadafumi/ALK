// MeshNormalBaker - MeshNormalBakerWindow.cs
// メニュー: Tools > Mesh Normal Baker
// メッシュの法線をUV空間へベイクして法線マップ(PNG)を出力する。単一メッシュのサブメッシュ選択に対応。
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MeshNormalBaker
{
    public class MeshNormalBakerWindow : EditorWindow
    {
        // 入力（GameObject または Mesh）
        Object _source;

        // ベイク設定
        int _resolutionIndex = 2; // 1024
        static readonly int[] kResolutions = { 256, 512, 1024, 2048, 4096 };
        static readonly string[] kResolutionLabels = { "256", "512", "1024", "2048", "4096" };
        NormalSpace _normalSpace = NormalSpace.Object;
        bool _flipY = false;
        int _dilationSteps = 4;

        // サブメッシュ選択
        readonly List<bool> _submeshMask = new List<bool>();
        Mesh _maskMesh; // マスクを構築した対象メッシュ

        RenderTexture _normalRT;
        string _bakedName;
        Vector2 _scroll;

        [MenuItem("Tools/Mesh Normal Baker")]
        public static void Open()
        {
            var win = GetWindow<MeshNormalBakerWindow>("Normal Baker");
            win.minSize = new Vector2(340, 520);
            win.Show();
        }

        void OnEnable() { Persist(true); }

        void OnDisable()
        {
            Persist(false);
            ReleaseRT(ref _normalRT);
        }

        static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); rt = null; }
        }

        Mesh ResolveMesh(out Matrix4x4 world, out Renderer renderer)
        {
            world = Matrix4x4.identity;
            renderer = null;
            if (_source is Mesh m) return m;
            if (_source is GameObject go)
            {
                world = go.transform.localToWorldMatrix;
                renderer = go.GetComponent<Renderer>();
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
                var smr = go.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null) return smr.sharedMesh;
            }
            return null;
        }

        void SyncSubmeshMask(Mesh mesh)
        {
            if (mesh == _maskMesh) return;
            _maskMesh = mesh;
            _submeshMask.Clear();
            int n = (mesh != null) ? Mathf.Max(1, mesh.subMeshCount) : 0;
            for (int i = 0; i < n; i++) _submeshMask.Add(true); // 既定は全選択
        }

        List<int> SelectedSubmeshes()
        {
            var list = new List<int>();
            for (int i = 0; i < _submeshMask.Count; i++) if (_submeshMask[i]) list.Add(i);
            return list;
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("入力", EditorStyles.boldLabel);
            _source = EditorGUILayout.ObjectField(
                new GUIContent("メッシュ / GameObject", "MeshFilter・SkinnedMeshRenderer を持つGameObject か Mesh を指定"),
                _source, typeof(Object), true);

            Mesh mesh = ResolveMesh(out Matrix4x4 world, out Renderer renderer);
            SyncSubmeshMask(mesh);

            if (mesh == null)
            {
                EditorGUILayout.HelpBox("メッシュを指定してください（GameObject か Mesh）。", MessageType.Info);
            }
            else if (mesh.uv == null || mesh.uv.Length == 0)
            {
                EditorGUILayout.HelpBox($"メッシュ '{mesh.name}' に UV がありません。ベイクにはUV展開が必要です。", MessageType.Warning);
            }
            else
            {
                // サブメッシュ選択
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"サブメッシュ（{_submeshMask.Count}）", EditorStyles.boldLabel);
                    if (GUILayout.Button("全選択", GUILayout.Width(60)))
                        for (int i = 0; i < _submeshMask.Count; i++) _submeshMask[i] = true;
                    if (GUILayout.Button("全解除", GUILayout.Width(60)))
                        for (int i = 0; i < _submeshMask.Count; i++) _submeshMask[i] = false;
                }
                var mats = (renderer != null) ? renderer.sharedMaterials : null;
                for (int i = 0; i < _submeshMask.Count; i++)
                {
                    string matName = (mats != null && i < mats.Length && mats[i] != null) ? $"  ({mats[i].name})" : "";
                    _submeshMask[i] = EditorGUILayout.ToggleLeft($"Submesh {i}{matName}", _submeshMask[i]);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("ベイク設定", EditorStyles.boldLabel);
            _resolutionIndex = EditorGUILayout.Popup("解像度", _resolutionIndex, kResolutionLabels);
            _normalSpace = (NormalSpace)EditorGUILayout.EnumPopup(
                new GUIContent("法線空間", "Object=配置に依存しない / World=シーン上の向きを反映"), _normalSpace);
            _dilationSteps = EditorGUILayout.IntSlider(
                new GUIContent("縁の埋め(px)", "UVアイランド外周を埋めてシームを軽減"), _dilationSteps, 0, 16);
            _flipY = EditorGUILayout.Toggle(new GUIContent("Y反転", "出力が上下反転する場合にON"), _flipY);

            bool canBake = mesh != null && mesh.uv != null && mesh.uv.Length > 0 && SelectedSubmeshes().Count > 0;
            using (new EditorGUI.DisabledScope(!canBake))
            {
                if (GUILayout.Button("法線マップをベイク", GUILayout.Height(28)))
                    Bake(mesh, world);
            }
            if (mesh != null && SelectedSubmeshes().Count == 0)
                EditorGUILayout.HelpBox("サブメッシュを1つ以上選択してください。", MessageType.Warning);

            // プレビュー
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            if (_normalRT != null)
            {
                float size = Mathf.Min(EditorGUIUtility.currentViewWidth - 30f, 300f);
                Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(r, _normalRT);
            }
            else
            {
                EditorGUILayout.HelpBox("ベイクするとここに法線マップが表示されます。", MessageType.None);
            }

            // 保存
            EditorGUILayout.Space();
            _chooseLocation = EditorGUILayout.ToggleLeft("保存時に場所を選ぶ（OFFで前回パスに上書き）", _chooseLocation);
            using (new EditorGUI.DisabledScope(_normalRT == null))
            {
                string ovPath = EditorPrefs.GetString(kSavePathKey, "");
                bool hasOv = !_chooseLocation && !string.IsNullOrEmpty(ovPath) && System.IO.File.Exists(ovPath);
                string saveLabel = hasOv ? $"上書き保存 ({System.IO.Path.GetFileName(ovPath)})" : "法線マップをPNG保存";
                if (GUILayout.Button(saveLabel, GUILayout.Height(24)))
                    Save();
            }

            EditorGUILayout.EndScrollView();
        }

        void Bake(Mesh mesh, Matrix4x4 world)
        {
            ReleaseRT(ref _normalRT);
            int res = kResolutions[_resolutionIndex];
            _normalRT = MeshNormalBakerCore.BakeNormalMap(
                mesh, SelectedSubmeshes(), res, _normalSpace, world, _flipY, _dilationSteps);
            _bakedName = mesh != null ? mesh.name : "Mesh";
            Repaint();
        }

        void Save()
        {
            if (_normalRT == null) return;
            string def = (!string.IsNullOrEmpty(_bakedName) ? _bakedName : "Mesh") + "_Normal.png";
            string path = ResolveSavePath("法線マップを保存", def);
            if (string.IsNullOrEmpty(path)) return;

            if (MeshNormalBakerCore.SaveRenderTextureToPng(_normalRT, path, true))
            {
                AssetDatabase.Refresh();
                Debug.Log($"[MeshNormalBaker] 保存しました: {path}");
                EditorUtility.RevealInFinder(path);
            }
        }

        // ---- パラメータの保持（EditorPrefs） ----
        static int Pi(string k, int v, bool load) { if (load) return EditorPrefs.GetInt(k, v); EditorPrefs.SetInt(k, v); return v; }
        static bool Pb(string k, bool v, bool load) { if (load) return EditorPrefs.GetBool(k, v); EditorPrefs.SetBool(k, v); return v; }
        void Persist(bool load)
        {
            const string P = "MeshNormalBaker.";
            _resolutionIndex = Pi(P + "res", _resolutionIndex, load);
            _normalSpace = (NormalSpace)Pi(P + "nspace", (int)_normalSpace, load);
            _flipY = Pb(P + "flipY", _flipY, load);
            _dilationSteps = Pi(P + "dilation", _dilationSteps, load);
            _chooseLocation = Pb(P + "chooseLoc", _chooseLocation, load);
        }

        // 保存先ディレクトリの記憶（共通キー）
        const string kLastSaveDirKey = "TextureTools.LastSaveDir";
        const string kSavePathKey = "MeshNormalBaker.LastSavePath";
        bool _chooseLocation = false;
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
