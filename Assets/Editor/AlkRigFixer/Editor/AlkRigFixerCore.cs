// AlkRigFixer - AlkRigFixerCore.cs
// ALK キャラクター(独自ボーン命名: hip / spine01 / arm1_L ...)の Humanoid Rig 設定を
// スクリプトから修正する中核ロジック。
//
// 背景:
//   ALK.fbx の Rig が StarterAssets の Armature アバター(Hips / Spine ...)を
//   "Copy From Other Avatar" で参照したままだと、ALK 側に 'Hips' が存在しないため
//   「Copied Avatar Rig Configuration mis-match. Transform 'Hips' for human bone 'Hips' not found」
//   が出る。アニメーション FBX が同じ参照を持っても同じエラーになる。
//
// 対処:
//   1. モデル FBX を "Create From This Model" に戻し、ALK 命名 → Unity Humanoid 名の対応表で
//      HumanDescription を明示的に与えて Avatar を再生成する。
//   2. アニメーション FBX は "Copy From Other Avatar" でモデルの Avatar を参照させる
//      (失敗した場合は各 FBX 単体で同じ対応表から Avatar を生成するフォールバックあり)。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlkRigFixer
{
    /// <summary>Humanoid ボーン名(HumanTrait.BoneName 準拠) と FBX 内のボーン名の対応。</summary>
    [System.Serializable]
    public class BoneMap
    {
        public string humanName; // 例: "Hips", "Left Thumb Proximal"
        public string boneName;  // 例: "hip", "finger_A1_1_L"

        public BoneMap(string human, string bone)
        {
            humanName = human;
            boneName = bone;
        }
    }

    /// <summary>アニメーション FBX に対する Avatar の与え方。</summary>
    public enum AnimationAvatarMode
    {
        CopyFromModel = 0,     // モデルの Avatar を Copy From Other Avatar で参照(推奨)
        CreateFromEachFile = 1 // 各 FBX 単体で対応表から Avatar を生成(フォールバック)
    }

    /// <summary>1ファイル分の処理結果。</summary>
    public class FixResult
    {
        public string path;
        public bool ok;
        public string message;

        public override string ToString()
        {
            return (ok ? "[OK] " : "[NG] ") + Path.GetFileName(path) + (string.IsNullOrEmpty(message) ? "" : " : " + message);
        }
    }

    public static class AlkRigFixerCore
    {
        public const string kDefaultModelPath = "Assets/Art/Character/ALK/ALK.fbx";
        public const string kDefaultAnimationFolder = "Assets/Art/Character/ALK/Animations";

        /// <summary>ALK リグ用のデフォルト対応表。ALK.fbx のボーン階層を元に作成。</summary>
        public static List<BoneMap> CreateDefaultBoneMaps()
        {
            return new List<BoneMap>
            {
                // 体幹: root > hip > pelvis > 脚 / hip > spine01 > spine02 > chest
                new BoneMap("Hips", "hip"),
                new BoneMap("Spine", "spine01"),
                new BoneMap("Chest", "spine02"),
                new BoneMap("UpperChest", "chest"),
                new BoneMap("Neck", "neck"),
                new BoneMap("Head", "head"),

                // 脚: leg01(腿) > leg02(脛) > leg03(足) > leg04(つま先)
                new BoneMap("LeftUpperLeg", "leg01_L"),
                new BoneMap("LeftLowerLeg", "leg02_L"),
                new BoneMap("LeftFoot", "leg03_L"),
                new BoneMap("LeftToes", "leg04_L"),
                new BoneMap("RightUpperLeg", "leg01_R"),
                new BoneMap("RightLowerLeg", "leg02_R"),
                new BoneMap("RightFoot", "leg03_R"),
                new BoneMap("RightToes", "leg04_R"),

                // 腕: clavicle(肩) > arm1(上腕) > arm2(前腕) > arm3(手)
                new BoneMap("LeftShoulder", "clavicle_L"),
                new BoneMap("LeftUpperArm", "arm1_L"),
                new BoneMap("LeftLowerArm", "arm2_L"),
                new BoneMap("LeftHand", "arm3_L"),
                new BoneMap("RightShoulder", "clavicle_R"),
                new BoneMap("RightUpperArm", "arm1_R"),
                new BoneMap("RightLowerArm", "arm2_R"),
                new BoneMap("RightHand", "arm3_R"),

                // 指: A1 = 親指(1..3 を使用、4 は先端)
                //      B1..B4 = 人差し指..小指(0 は中手骨、1..3 を使用、4 は先端)
                new BoneMap("Left Thumb Proximal", "finger_A1_1_L"),
                new BoneMap("Left Thumb Intermediate", "finger_A1_2_L"),
                new BoneMap("Left Thumb Distal", "finger_A1_3_L"),
                new BoneMap("Left Index Proximal", "finger_B1_1_L"),
                new BoneMap("Left Index Intermediate", "finger_B1_2_L"),
                new BoneMap("Left Index Distal", "finger_B1_3_L"),
                new BoneMap("Left Middle Proximal", "finger_B2_1_L"),
                new BoneMap("Left Middle Intermediate", "finger_B2_2_L"),
                new BoneMap("Left Middle Distal", "finger_B2_3_L"),
                new BoneMap("Left Ring Proximal", "finger_B3_1_L"),
                new BoneMap("Left Ring Intermediate", "finger_B3_2_L"),
                new BoneMap("Left Ring Distal", "finger_B3_3_L"),
                new BoneMap("Left Little Proximal", "finger_B4_1_L"),
                new BoneMap("Left Little Intermediate", "finger_B4_2_L"),
                new BoneMap("Left Little Distal", "finger_B4_3_L"),

                new BoneMap("Right Thumb Proximal", "finger_A1_1_R"),
                new BoneMap("Right Thumb Intermediate", "finger_A1_2_R"),
                new BoneMap("Right Thumb Distal", "finger_A1_3_R"),
                new BoneMap("Right Index Proximal", "finger_B1_1_R"),
                new BoneMap("Right Index Intermediate", "finger_B1_2_R"),
                new BoneMap("Right Index Distal", "finger_B1_3_R"),
                new BoneMap("Right Middle Proximal", "finger_B2_1_R"),
                new BoneMap("Right Middle Intermediate", "finger_B2_2_R"),
                new BoneMap("Right Middle Distal", "finger_B2_3_R"),
                new BoneMap("Right Ring Proximal", "finger_B3_1_R"),
                new BoneMap("Right Ring Intermediate", "finger_B3_2_R"),
                new BoneMap("Right Ring Distal", "finger_B3_3_R"),
                new BoneMap("Right Little Proximal", "finger_B4_1_R"),
                new BoneMap("Right Little Intermediate", "finger_B4_2_R"),
                new BoneMap("Right Little Distal", "finger_B4_3_R"),
            };
        }

        // ------------------------------------------------------------------
        // 情報取得
        // ------------------------------------------------------------------

        /// <summary>フォルダ直下の FBX パスを列挙(サブフォルダは含まない)。</summary>
        public static List<string> FindFbxFiles(string folder)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return result;
            string normalized = folder.Replace('\\', '/').TrimEnd('/');
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                var dir = Path.GetDirectoryName(path);
                if (dir == null || dir.Replace('\\', '/') != normalized) continue;
                result.Add(path);
            }
            result.Sort();
            return result;
        }

        /// <summary>FBX の全 Transform 名を集める(重複は除去)。</summary>
        public static HashSet<string> CollectTransformNames(string fbxPath)
        {
            var names = new HashSet<string>();
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (go == null) return names;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) names.Add(t.name);
            return names;
        }

        /// <summary>対応表のボーンが FBX に存在するか検証し、不足しているものを返す。</summary>
        public static List<string> FindMissingBones(string fbxPath, List<BoneMap> maps)
        {
            var names = CollectTransformNames(fbxPath);
            var missing = new List<string>();
            foreach (var m in maps)
            {
                if (string.IsNullOrEmpty(m.boneName)) continue;
                if (!names.Contains(m.boneName)) missing.Add(m.humanName + " -> " + m.boneName);
            }
            return missing;
        }

        /// <summary>FBX に含まれる Avatar サブアセットを取得。</summary>
        public static Avatar LoadAvatar(string fbxPath)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var av = a as Avatar;
                if (av != null) return av;
            }
            return null;
        }

        /// <summary>ModelImporter が保持する Rig インポートのエラー/警告文字列(内部プロパティ)。</summary>
        public static string GetRigImportMessages(ModelImporter importer)
        {
            if (importer == null) return "";
            var so = new SerializedObject(importer);
            var sb = new StringBuilder();
            var err = so.FindProperty("m_RigImportErrors");
            var warn = so.FindProperty("m_RigImportWarnings");
            if (err != null && !string.IsNullOrEmpty(err.stringValue)) sb.Append("Error: ").Append(err.stringValue.Trim());
            if (warn != null && !string.IsNullOrEmpty(warn.stringValue))
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append("Warning: ").Append(warn.stringValue.Trim());
            }
            return sb.ToString();
        }

        /// <summary>現在の Rig 設定を 1 行で要約。</summary>
        public static string DescribeRig(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return "ModelImporter なし";
            var av = LoadAvatar(fbxPath);
            var sb = new StringBuilder();
            sb.Append(importer.animationType).Append(" / ").Append(importer.avatarSetup);
            if (importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther)
                sb.Append(" (source: ").Append(importer.sourceAvatar ? AssetDatabase.GetAssetPath(importer.sourceAvatar) : "なし").Append(")");
            sb.Append(" / Avatar: ");
            if (av == null) sb.Append("なし");
            else sb.Append(av.isValid ? "valid" : "INVALID").Append(av.isHuman ? ", human" : ", not human");
            var msg = GetRigImportMessages(importer);
            if (!string.IsNullOrEmpty(msg)) sb.Append(" / ").Append(msg);
            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // 修正処理
        // ------------------------------------------------------------------

        /// <summary>
        /// FBX を "Create From This Model" にし、対応表から HumanDescription を明示的に与えて
        /// Avatar を再生成する。モデル本体・アニメーション単体どちらにも使える。
        /// </summary>
        public static FixResult CreateAvatarFromMapping(string fbxPath, List<BoneMap> maps)
        {
            var r = new FixResult { path = fbxPath };
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) { r.message = "ModelImporter が取得できません"; return r; }

            // 1. 古い Copy 参照を外し、Humanoid / Create From This Model で再インポートして階層を確定させる。
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.SaveAndReimport();

            // 2. 再インポート後の階層から Skeleton と Human 対応を構築する。
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (go == null) { r.message = "GameObject をロードできません"; return r; }

            var names = new HashSet<string>();
            var skeleton = new List<SkeletonBone>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                names.Add(t.name);
                skeleton.Add(new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale,
                });
            }

            var human = new List<HumanBone>();
            var missing = new List<string>();
            foreach (var m in maps)
            {
                if (string.IsNullOrEmpty(m.boneName) || string.IsNullOrEmpty(m.humanName)) continue;
                if (!names.Contains(m.boneName)) { missing.Add(m.humanName + " -> " + m.boneName); continue; }
                human.Add(new HumanBone
                {
                    humanName = m.humanName,
                    boneName = m.boneName,
                    limit = new HumanLimit { useDefaultValues = true },
                });
            }

            importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            var hd = importer.humanDescription;
            hd.human = human.ToArray();
            hd.skeleton = skeleton.ToArray();
            hd.upperArmTwist = 0.5f;
            hd.lowerArmTwist = 0.5f;
            hd.upperLegTwist = 0.5f;
            hd.lowerLegTwist = 0.5f;
            hd.armStretch = 0.05f;
            hd.legStretch = 0.05f;
            hd.feetSpacing = 0f;
            hd.hasTranslationDoF = false;
            importer.humanDescription = hd;
            importer.SaveAndReimport();

            // 3. 生成結果の検証。
            var av = LoadAvatar(fbxPath);
            var sb = new StringBuilder();
            if (missing.Count > 0) sb.Append("未検出ボーン: ").Append(string.Join(", ", missing.ToArray())).Append(" / ");
            var msg = GetRigImportMessages(AssetImporter.GetAtPath(fbxPath) as ModelImporter);
            if (!string.IsNullOrEmpty(msg)) sb.Append(msg).Append(" / ");
            if (av == null) { sb.Append("Avatar が生成されませんでした"); r.message = sb.ToString(); return r; }
            r.ok = av.isValid && av.isHuman;
            sb.Append("Avatar ").Append(av.isValid ? "valid" : "INVALID").Append(av.isHuman ? " human" : " not-human");
            r.message = sb.ToString();
            return r;
        }

        /// <summary>アニメーション FBX を "Copy From Other Avatar" で指定 Avatar に紐付ける。</summary>
        public static FixResult CopyAvatarTo(string fbxPath, Avatar source)
        {
            var r = new FixResult { path = fbxPath };
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) { r.message = "ModelImporter が取得できません"; return r; }
            if (source == null || !source.isValid) { r.message = "コピー元 Avatar が無効です"; return r; }

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = source;
            importer.SaveAndReimport();

            importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            var msg = GetRigImportMessages(importer);
            bool hasError = msg.Contains("Error:");
            bool humanClip = HasHumanClip(fbxPath);
            r.ok = !hasError && humanClip;
            r.message = (string.IsNullOrEmpty(msg) ? "" : msg + " / ") + (humanClip ? "Humanoid クリップ生成済み" : "Humanoid クリップなし");
            return r;
        }

        /// <summary>FBX 内に Humanoid として取り込まれた AnimationClip があるか。</summary>
        public static bool HasHumanClip(string fbxPath)
        {
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                var clip = a as AnimationClip;
                if (clip == null || clip.name.StartsWith("__preview__")) continue;
                if (clip.isHumanMotion) return true;
            }
            return false;
        }

        /// <summary>
        /// 一括修正: モデル Avatar を再生成し、フォルダ内のアニメーション FBX を紐付ける。
        /// CopyFromModel で失敗したファイルは、指定があれば CreateFromEachFile にフォールバックする。
        /// </summary>
        public static List<FixResult> FixAll(string modelPath, string animationFolder, List<BoneMap> maps, AnimationAvatarMode mode, bool fallbackOnFailure)
        {
            var results = new List<FixResult>();
            try
            {
                EditorUtility.DisplayProgressBar("ALK Rig Fixer", Path.GetFileName(modelPath), 0f);
                var modelResult = CreateAvatarFromMapping(modelPath, maps);
                results.Add(modelResult);
                var modelAvatar = LoadAvatar(modelPath);

                var files = FindFbxFiles(animationFolder);
                for (int i = 0; i < files.Count; i++)
                {
                    var f = files[i];
                    if (f == modelPath) continue;
                    EditorUtility.DisplayProgressBar("ALK Rig Fixer", Path.GetFileName(f), (i + 1f) / (files.Count + 1f));
                    results.Add(FixAnimation(f, maps, mode, fallbackOnFailure, modelAvatar));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            return results;
        }

        // ------------------------------------------------------------------
        // ループ設定
        // ------------------------------------------------------------------

        /// <summary>ファイル名からループさせるべきかの既定値を返す(StarterAssets の設定に合わせる)。</summary>
        public static bool DefaultLoopFor(string fbxPath)
        {
            var name = Path.GetFileNameWithoutExtension(fbxPath);
            // Jump--Jump(JumpStart) だけは単発再生。Idle / Walk / Run / InAir はループ。
            return name.IndexOf("Jump--Jump", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// FBX 内の全クリップに Loop Time と Root Transform の Bake 設定を適用する。
        /// clipAnimations が未定義(既定クリップのまま)の場合は defaultClipAnimations を元に作成する。
        /// </summary>
        public static FixResult ApplyLoopSettings(string fbxPath, bool loop, bool bakeOrientation, bool bakePositionY, bool bakePositionXZ)
        {
            var r = new FixResult { path = fbxPath };
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) { r.message = "ModelImporter が取得できません"; return r; }

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) { r.message = "クリップがありません"; return r; }

            foreach (var c in clips)
            {
                c.loopTime = loop;
                c.loopPose = false; // ループ姿勢の補間。必要なら手動で ON にする
                // Root Transform Rotation / Position(Y) / Position(XZ) の Bake Into Pose
                c.lockRootRotation = bakeOrientation;
                c.lockRootHeightY = bakePositionY;
                c.lockRootPositionXZ = bakePositionXZ;
                c.keepOriginalOrientation = true;
                c.keepOriginalPositionY = true;
                c.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();

            r.ok = true;
            r.message = clips.Length + " クリップ: Loop Time=" + (loop ? "ON" : "OFF")
                        + ", Bake Rot=" + (bakeOrientation ? "ON" : "OFF")
                        + ", Bake Y=" + (bakePositionY ? "ON" : "OFF")
                        + ", Bake XZ=" + (bakePositionXZ ? "ON" : "OFF");
            return r;
        }

        /// <summary>現在のループ設定を 1 行で要約。</summary>
        public static string DescribeLoop(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return "ModelImporter なし";
            var clips = importer.clipAnimations;
            bool isDefault = clips == null || clips.Length == 0;
            if (isDefault) clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0) return "クリップなし";
            var sb = new StringBuilder();
            if (isDefault) sb.Append("(既定クリップ) ");
            foreach (var c in clips)
                sb.Append(c.name).Append(": Loop=").Append(c.loopTime ? "ON" : "OFF").Append("  ");
            return sb.ToString().TrimEnd();
        }

        /// <summary>アニメーション FBX 1 件を指定モードで修正する。</summary>
        public static FixResult FixAnimation(string fbxPath, List<BoneMap> maps, AnimationAvatarMode mode, bool fallbackOnFailure, Avatar modelAvatar)
        {
            if (mode == AnimationAvatarMode.CopyFromModel)
            {
                if (modelAvatar == null || !modelAvatar.isValid)
                {
                    if (!fallbackOnFailure)
                        return new FixResult { path = fbxPath, message = "モデル Avatar が無効なため Copy できません" };
                    var fb0 = CreateAvatarFromMapping(fbxPath, maps);
                    fb0.message = "モデル Avatar 無効 → 個別生成: " + fb0.message;
                    return fb0;
                }

                var r = CopyAvatarTo(fbxPath, modelAvatar);
                if (r.ok || !fallbackOnFailure) return r;

                var fb = CreateAvatarFromMapping(fbxPath, maps);
                fb.message = "Copy 失敗(" + r.message + ") → 個別生成: " + fb.message;
                return fb;
            }
            return CreateAvatarFromMapping(fbxPath, maps);
        }
    }
}
