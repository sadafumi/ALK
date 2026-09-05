// AlkRigFixer - AlkRigFixerWindow.cs
// メニュー: Tools > ALK > Rig Fixer
// ALK キャラクターと同梱アニメーション FBX の Humanoid Rig 設定をまとめて修正する暫定ツール。
// 「Copied Avatar Rig Configuration mis-match. Transform 'Hips' for human bone 'Hips' not found」対策。
// あわせてクリップの Loop Time / Root Transform Bake Into Pose を一括設定できる。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlkRigFixer
{
    public class AlkRigFixerWindow : EditorWindow
    {
        const string kPrefModelPath = "AlkRigFixer.ModelPath";
        const string kPrefAnimFolder = "AlkRigFixer.AnimFolder";
        const string kPrefMode = "AlkRigFixer.Mode";
        const string kPrefFallback = "AlkRigFixer.Fallback";

        string _modelPath = AlkRigFixerCore.kDefaultModelPath;
        string _animFolder = AlkRigFixerCore.kDefaultAnimationFolder;
        AnimationAvatarMode _mode = AnimationAvatarMode.CopyFromModel;
        bool _fallback = true;

        List<BoneMap> _maps;
        bool _showMaps = false;

        // ループ設定
        bool _showLoop = true;
        bool _bakeOrientation = true;
        bool _bakePositionY = true;
        bool _bakePositionXZ = true;
        readonly Dictionary<string, bool> _loopByFile = new Dictionary<string, bool>();
        Vector2 _loopScroll;
        Vector2 _mapScroll;
        Vector2 _logScroll;
        string _log = "";

        [MenuItem("Tools/ALK/Rig Fixer")]
        static void Open()
        {
            var w = GetWindow<AlkRigFixerWindow>("ALK Rig Fixer");
            w.minSize = new Vector2(520, 480);
        }

        /// <summary>ウィンドウを開かず、既定設定で一括修正を実行するショートカット。</summary>
        [MenuItem("Tools/ALK/Rig Fixer - 既定設定で一括修正")]
        static void QuickFix()
        {
            if (!EditorUtility.DisplayDialog("ALK Rig Fixer",
                    AlkRigFixerCore.kDefaultModelPath + " の Avatar を再生成し、\n" +
                    AlkRigFixerCore.kDefaultAnimationFolder + " 内の FBX をその Avatar に紐付けます。\n\n" +
                    "各 FBX の Rig 設定(.meta)が書き換わります。実行しますか?", "実行", "キャンセル"))
                return;

            var results = AlkRigFixerCore.FixAll(
                AlkRigFixerCore.kDefaultModelPath,
                AlkRigFixerCore.kDefaultAnimationFolder,
                AlkRigFixerCore.CreateDefaultBoneMaps(),
                AnimationAvatarMode.CopyFromModel,
                true);
            var text = FormatResults(results);
            Debug.Log("[ALK Rig Fixer]\n" + text);
            var w = GetWindow<AlkRigFixerWindow>("ALK Rig Fixer");
            w._log = text;
        }

        void OnEnable()
        {
            _modelPath = EditorPrefs.GetString(kPrefModelPath, AlkRigFixerCore.kDefaultModelPath);
            _animFolder = EditorPrefs.GetString(kPrefAnimFolder, AlkRigFixerCore.kDefaultAnimationFolder);
            _mode = (AnimationAvatarMode)EditorPrefs.GetInt(kPrefMode, (int)AnimationAvatarMode.CopyFromModel);
            _fallback = EditorPrefs.GetBool(kPrefFallback, true);
            if (_maps == null) _maps = AlkRigFixerCore.CreateDefaultBoneMaps();
        }

        void OnDisable()
        {
            EditorPrefs.SetString(kPrefModelPath, _modelPath);
            EditorPrefs.SetString(kPrefAnimFolder, _animFolder);
            EditorPrefs.SetInt(kPrefMode, (int)_mode);
            EditorPrefs.SetBool(kPrefFallback, _fallback);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Rig エラー「Transform 'Hips' for human bone 'Hips' not found」の暫定修正ツールです。\n" +
                "モデル FBX を Create From This Model + ALK 用ボーン対応表で Avatar 再生成し、\n" +
                "アニメーション FBX をその Avatar に紐付け直します。各 FBX の .meta が書き換わります。",
                MessageType.Info);

            DrawTargets();
            EditorGUILayout.Space();
            DrawOptions();
            EditorGUILayout.Space();
            DrawBoneMaps();
            EditorGUILayout.Space();
            DrawActions();
            EditorGUILayout.Space();
            DrawLoopSettings();
            EditorGUILayout.Space();
            DrawLog();
        }

        // ------------------------------------------------------------------
        // ループ設定
        // ------------------------------------------------------------------
        void DrawLoopSettings()
        {
            _showLoop = EditorGUILayout.Foldout(_showLoop, "ループ設定 (Loop Time / Root Transform Bake)", true);
            if (!_showLoop) return;

            EditorGUILayout.HelpBox(
                "ALK 側の FBX はクリップ設定が未定義のため Loop Time が OFF になっています。\n" +
                "ここでファイルごとに ON/OFF を選んで適用すると、Inspector の Animation タブで手動設定したのと同じ状態になります。",
                MessageType.None);

            _bakeOrientation = EditorGUILayout.ToggleLeft("Root Transform Rotation を Bake Into Pose (向きが回らないようにする)", _bakeOrientation);
            _bakePositionY = EditorGUILayout.ToggleLeft("Root Transform Position (Y) を Bake Into Pose (上下に動かない)", _bakePositionY);
            _bakePositionXZ = EditorGUILayout.ToggleLeft("Root Transform Position (XZ) を Bake Into Pose (CharacterController で移動する場合は ON)", _bakePositionXZ);

            var files = AlkRigFixerCore.FindFbxFiles(_animFolder);
            using (var sv = new EditorGUILayout.ScrollViewScope(_loopScroll, GUILayout.Height(Mathf.Min(160, 20 * files.Count + 8))))
            {
                _loopScroll = sv.scrollPosition;
                foreach (var f in files)
                {
                    if (f == _modelPath) continue;
                    bool cur;
                    if (!_loopByFile.TryGetValue(f, out cur)) cur = AlkRigFixerCore.DefaultLoopFor(f);
                    _loopByFile[f] = EditorGUILayout.ToggleLeft("Loop  " + Path.GetFileName(f), cur);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("既定 (Jump--Jump 以外 ON)"))
                {
                    _loopByFile.Clear();
                }
                if (GUILayout.Button("全て ON"))
                {
                    foreach (var f in files) _loopByFile[f] = true;
                }
                if (GUILayout.Button("全て OFF"))
                {
                    foreach (var f in files) _loopByFile[f] = false;
                }
            }

            using (new EditorGUI.DisabledScope(files.Count == 0))
            {
                if (GUILayout.Button("ループ設定を適用", GUILayout.Height(24)))
                {
                    if (Confirm("フォルダ内の FBX のクリップ設定(Loop Time / Bake Into Pose)を書き換えます。"))
                    {
                        var results = new List<FixResult>();
                        try
                        {
                            for (int i = 0; i < files.Count; i++)
                            {
                                if (files[i] == _modelPath) continue;
                                EditorUtility.DisplayProgressBar("ALK Rig Fixer", Path.GetFileName(files[i]), (float)i / Mathf.Max(1, files.Count));
                                bool loop;
                                if (!_loopByFile.TryGetValue(files[i], out loop)) loop = AlkRigFixerCore.DefaultLoopFor(files[i]);
                                results.Add(AlkRigFixerCore.ApplyLoopSettings(files[i], loop, _bakeOrientation, _bakePositionY, _bakePositionXZ));
                            }
                        }
                        finally
                        {
                            EditorUtility.ClearProgressBar();
                        }
                        AssetDatabase.SaveAssets();
                        SetLog(FormatResults(results));
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // 対象
        // ------------------------------------------------------------------
        void DrawTargets()
        {
            EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var modelObj = AssetDatabase.LoadAssetAtPath<GameObject>(_modelPath);
                var picked = EditorGUILayout.ObjectField("モデル FBX", modelObj, typeof(GameObject), false) as GameObject;
                if (picked != modelObj && picked != null)
                {
                    var p = AssetDatabase.GetAssetPath(picked);
                    if (p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) _modelPath = p;
                }
            }
            _modelPath = EditorGUILayout.TextField(" ", _modelPath);

            using (new EditorGUILayout.HorizontalScope())
            {
                var folderObj = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_animFolder);
                var picked = EditorGUILayout.ObjectField("アニメーション フォルダ", folderObj, typeof(DefaultAsset), false) as DefaultAsset;
                if (picked != folderObj && picked != null)
                {
                    var p = AssetDatabase.GetAssetPath(picked);
                    if (AssetDatabase.IsValidFolder(p)) _animFolder = p;
                }
            }
            _animFolder = EditorGUILayout.TextField(" ", _animFolder);

            var files = AlkRigFixerCore.FindFbxFiles(_animFolder);
            EditorGUILayout.LabelField(" ", "フォルダ直下の FBX: " + files.Count + " 件");
        }

        // ------------------------------------------------------------------
        // オプション
        // ------------------------------------------------------------------
        void DrawOptions()
        {
            EditorGUILayout.LabelField("アニメーション FBX の Avatar", EditorStyles.boldLabel);
            _mode = (AnimationAvatarMode)EditorGUILayout.EnumPopup("方式", _mode);
            if (_mode == AnimationAvatarMode.CopyFromModel)
            {
                EditorGUILayout.HelpBox("モデルの Avatar を Copy From Other Avatar で参照します(推奨)。", MessageType.None);
                _fallback = EditorGUILayout.ToggleLeft("Copy に失敗した FBX は個別に Avatar を生成する", _fallback);
            }
            else
            {
                EditorGUILayout.HelpBox("各 FBX で Create From This Model + 対応表を適用します。Copy でエラーが出続ける場合の代替。", MessageType.None);
            }
        }

        // ------------------------------------------------------------------
        // ボーン対応表
        // ------------------------------------------------------------------
        void DrawBoneMaps()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _showMaps = EditorGUILayout.Foldout(_showMaps, "ボーン対応表 (Humanoid → FBX ボーン名) : " + _maps.Count + " 件", true);
                if (GUILayout.Button("既定に戻す", GUILayout.Width(90)))
                    _maps = AlkRigFixerCore.CreateDefaultBoneMaps();
            }
            if (!_showMaps) return;

            using (var sv = new EditorGUILayout.ScrollViewScope(_mapScroll, GUILayout.Height(220)))
            {
                _mapScroll = sv.scrollPosition;
                int remove = -1;
                for (int i = 0; i < _maps.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        _maps[i].humanName = EditorGUILayout.TextField(_maps[i].humanName);
                        EditorGUILayout.LabelField("→", GUILayout.Width(20));
                        _maps[i].boneName = EditorGUILayout.TextField(_maps[i].boneName);
                        if (GUILayout.Button("×", GUILayout.Width(22))) remove = i;
                    }
                }
                if (remove >= 0) _maps.RemoveAt(remove);
            }
            if (GUILayout.Button("行を追加"))
                _maps.Add(new BoneMap("", ""));
        }

        // ------------------------------------------------------------------
        // 実行
        // ------------------------------------------------------------------
        void DrawActions()
        {
            EditorGUILayout.LabelField("実行", EditorStyles.boldLabel);

            bool modelOk = File.Exists(_modelPath);
            bool folderOk = AssetDatabase.IsValidFolder(_animFolder);

            if (GUILayout.Button("現状を確認 (Rig 設定 / 不足ボーン)"))
                Inspect();

            using (new EditorGUI.DisabledScope(!modelOk))
            {
                if (GUILayout.Button("1. モデルの Avatar を再生成"))
                {
                    if (Confirm("モデル FBX の Rig 設定を Create From This Model にし、対応表から Avatar を再生成します。"))
                    {
                        var r = AlkRigFixerCore.CreateAvatarFromMapping(_modelPath, _maps);
                        AssetDatabase.SaveAssets();
                        SetLog(FormatResults(new List<FixResult> { r }));
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!folderOk))
            {
                if (GUILayout.Button("2. アニメーション FBX を修正"))
                {
                    if (Confirm("フォルダ内の FBX の Rig 設定を書き換えます。"))
                    {
                        var results = new List<FixResult>();
                        var modelAvatar = AlkRigFixerCore.LoadAvatar(_modelPath);
                        var files = AlkRigFixerCore.FindFbxFiles(_animFolder);
                        try
                        {
                            for (int i = 0; i < files.Count; i++)
                            {
                                if (files[i] == _modelPath) continue;
                                EditorUtility.DisplayProgressBar("ALK Rig Fixer", Path.GetFileName(files[i]), (float)i / Mathf.Max(1, files.Count));
                                results.Add(AlkRigFixerCore.FixAnimation(files[i], _maps, _mode, _fallback, modelAvatar));
                            }
                        }
                        finally
                        {
                            EditorUtility.ClearProgressBar();
                        }
                        AssetDatabase.SaveAssets();
                        SetLog(FormatResults(results));
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!modelOk || !folderOk))
            {
                GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
                if (GUILayout.Button("一括修正 (1 → 2)", GUILayout.Height(28)))
                {
                    if (Confirm("モデル FBX とフォルダ内の FBX すべての Rig 設定を書き換えます。"))
                    {
                        var results = AlkRigFixerCore.FixAll(_modelPath, _animFolder, _maps, _mode, _fallback);
                        SetLog(FormatResults(results));
                    }
                }
                GUI.backgroundColor = Color.white;
            }
        }

        bool Confirm(string body)
        {
            return EditorUtility.DisplayDialog("ALK Rig Fixer", body + "\n\n(.meta が変更されます。元に戻すには git で .meta を戻してください)", "実行", "キャンセル");
        }

        /// <summary>設定は変更せず、現在の Rig 状態と対応表の不足ボーンを一覧にする。</summary>
        void Inspect()
        {
            var sb = new StringBuilder();
            var targets = new List<string>();
            if (File.Exists(_modelPath)) targets.Add(_modelPath);
            foreach (var f in AlkRigFixerCore.FindFbxFiles(_animFolder))
                if (f != _modelPath) targets.Add(f);

            foreach (var p in targets)
            {
                sb.Append(Path.GetFileName(p)).Append("\n    ").Append(AlkRigFixerCore.DescribeRig(p)).Append('\n');
                if (p != _modelPath)
                    sb.Append("    ループ: ").Append(AlkRigFixerCore.DescribeLoop(p)).Append('\n');
                var missing = AlkRigFixerCore.FindMissingBones(p, _maps);
                if (missing.Count > 0)
                    sb.Append("    不足ボーン: ").Append(string.Join(", ", missing.ToArray())).Append('\n');
            }
            if (targets.Count == 0) sb.Append("対象がありません");
            SetLog(sb.ToString());
        }

        void SetLog(string text)
        {
            _log = text;
            Debug.Log("[ALK Rig Fixer]\n" + text);
            Repaint();
        }

        static string FormatResults(List<FixResult> results)
        {
            var sb = new StringBuilder();
            int ok = 0;
            foreach (var r in results)
            {
                sb.Append(r).Append('\n');
                if (r.ok) ok++;
            }
            sb.Append("成功 ").Append(ok).Append(" / ").Append(results.Count);
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // ログ
        // ------------------------------------------------------------------
        void DrawLog()
        {
            EditorGUILayout.LabelField("結果", EditorStyles.boldLabel);
            using (var sv = new EditorGUILayout.ScrollViewScope(_logScroll, GUILayout.ExpandHeight(true)))
            {
                _logScroll = sv.scrollPosition;
                EditorGUILayout.TextArea(string.IsNullOrEmpty(_log) ? "(未実行)" : _log, GUILayout.ExpandHeight(true));
            }
        }
    }
}
