//using System;
//using UnityEditor;
//using UnityEditor.Rendering;
//using UnityEngine;
//using UnityEngine.Rendering;
//using UnityEngine.Rendering.Universal;
//using RenderQueue = UnityEngine.Rendering.RenderQueue;

//public class CharacterEditorGUI : ShaderGUI
//{
//    protected MaterialProperty surfaceTypeProp { get; set; }
//    protected MaterialProperty shaderType { get; set; }

//    protected MaterialProperty blendModeProp { get; set; }

//    protected MaterialProperty cullingProp { get; set; }
//    protected MaterialProperty ztestProp { get; set; }
//    protected MaterialProperty zwriteProp { get; set; }
//    protected MaterialProperty alphaClipProp { get; set; }

//    protected MaterialProperty castShadowsProp { get; set; }
//    protected MaterialProperty receiveShadowsProp { get; set; }

//    protected MaterialProperty alphaCutoffProp { get; set; }

//    protected MaterialProperty queueOffsetProp { get; set; }
//    protected MaterialProperty queueControlProp { get; set; }

//    protected MaterialProperty albedoTex { get; set; }
//    protected MaterialProperty albedoColor { get; set; }
//    protected MaterialProperty alphaCutOff { get; set; }

//    protected MaterialProperty shadowTex { get; set; }
//    protected MaterialProperty shadowRampTex { get; set; }
//    protected MaterialProperty shadingArea { get; set; }
//    protected MaterialProperty shadingAreaGradation { get; set; }
//    protected MaterialProperty shadingAreaCorrection { get; set; }

//    protected MaterialProperty shadowScale { get; set; }


//    protected MaterialProperty packing1Tex { get; set; }
//    protected MaterialProperty packing2Tex { get; set; }
//    protected MaterialProperty rampMaskTex { get; set; }
//    protected MaterialProperty rampTex { get; set; }
//    protected MaterialProperty useRampMask { get; set; }

//    //protected MaterialProperty useMatCap { get; set; }
//    //protected MaterialProperty matcapTex { get; set; }
//    //protected MaterialProperty matcapColor { get; set; }
//    //protected MaterialProperty matcapPawer { get; set; }
//    //protected MaterialProperty matcapMaskInfluence { get; set; }
//    //protected MaterialProperty matcapBlendMode { get; set; }


//    //protected MaterialProperty rimLightColor { get; set; }
//    //protected MaterialProperty rimLightPawer { get; set; }
//    //protected MaterialProperty rimLightInsideMask { get; set; }
//    //protected MaterialProperty rimLightMaskLevel { get; set; }


//    //protected MaterialProperty brightRimAlpha { get; set; }
//    //protected MaterialProperty brightRimThickness { get; set; }
//    //protected MaterialProperty brightRimThreshold { get; set; }
//    //protected MaterialProperty brightRimSmooth { get; set; }
//    //protected MaterialProperty brightRimColor { get; set; }

//    //protected MaterialProperty brightRimOutlineThreshold { get; set; }
//    //protected MaterialProperty brightRimOutlineSmooth { get; set; }

//    //protected MaterialProperty highlightRimColorThreshold { get; set; }
//    //protected MaterialProperty highlightRimColor { get; set; }

//    //protected MaterialProperty pointRimLightPawer { get; set; }
//    //protected MaterialProperty pointRimLightInsideMask { get; set; }
//    //protected MaterialProperty pointRimLightMaskLevel { get; set; }

//    //protected MaterialProperty uvTimeScale { get; set; }

//    //protected MaterialProperty useRamp { get; set; }
//    //protected MaterialProperty rampStep { get; set; }
//    //protected MaterialProperty rampSlider { get; set; }

//    //protected MaterialProperty matallicTex { get; set; }
//    //protected MaterialProperty matallicBlendMode { get; set; }

//    //protected MaterialProperty specularColor { get; set; }
//    //protected MaterialProperty specularPower { get; set; }
    
//    protected MaterialProperty useOutline { get; set; }
//    protected MaterialProperty useOutlineTex { get; set; }
//    protected MaterialProperty outlineColorTex { get; set; }
//    protected MaterialProperty outlineWidth { get; set; }
//    protected MaterialProperty debugOutlineWidth { get; set; }
//    protected MaterialProperty outlineColor { get; set; }

//    protected MaterialProperty pointLight { get; set; }

//    //protected MaterialProperty useCubeMap { get; set; }
//    //protected MaterialProperty cubeMapTexture { get; set; }
//    //protected MaterialProperty cubeMapColor { get; set; }

//    protected MaterialProperty stencilNo { get; set; }

//    private int rampStepIndex = 0;

//    const string STR_ONSTATE = "Active";
//    const string STR_OFFSTATE = "Off";
//    public GUILayoutOption[] shortButtonStyle = new GUILayoutOption[] { GUILayout.Width(130) };
//    public GUILayoutOption[] middleButtonStyle = new GUILayoutOption[] { GUILayout.Width(130) };

//    static bool _BaseSettings_Foldout = true;
//    static bool _Outline_Foldout = true;
//    static bool _RenderingSettings_Foldout = true;
//    static bool _Debug_Foldout = true;
//    const string srpDefaultLightModeName = "UniversalForwardOnly";
//    const string srpDepthLightModeName = "DepthOnlySub";
//    const string srpDepthNormalLightModeName = "DepthNormalsOnly";

//    public enum SurfaceType
//    {
//        Opaque,
//        Transparent
//    }
//    public enum RenderFace
//    {
//        Front = 2,
//        Back = 1,
//        Both = 0
//    }
//    public enum BlendMode
//    {
//        Alpha,
//        Premultiply,
//        Additive,
//        Multiply
//    }
//    public enum QueueControl
//    {
//        Auto = 0,
//        UserOverride = 1
//    }
//    public enum ShaderType
//    {
//        All,
//        Face,
//        Hair,
//        Prop,
//        Eyes,
//        Simple
//    }
//    public enum RampStep
//    {
//        One,
//        Two,
//        Three,
//        Four,
//        Five,
//        Six
//    }
//    protected static class Styles
//    {
//        public static readonly string[] surfaceTypeNames = Enum.GetNames(typeof(SurfaceType));
//        public static readonly GUIContent surfaceType = EditorGUIUtility.TrTextContent("サーフェスタイプ", "テクスチャの表面タイプを選択します。不透明または透明を選択します。");

//        public static readonly GUIContent alphaClipText = EditorGUIUtility.TrTextContent("Alpha Clipping", "マテリアルをカットアウト シェーダーのように動作させます。これを使用して、不透明領域と透明領域の間にハード エッジのある透明効果を作成します。アルファがマテリアル全体で一定の場合は使用しないでください。この場合、有効にすると視覚的なアーティファクトが発生し、MSAA で使用すると (AlphaToMask のため) 不要なパフォーマンス コストが追加されることがあります。");
//        public static readonly GUIContent alphaClipThresholdText = EditorGUIUtility.TrTextContent("Threshold", "アルファ クリッピングの開始位置を設定します。値が高いほど、クリッピングの開始時の効果が明るくなります。");

//        public static readonly string[] blendModeNames = Enum.GetNames(typeof(BlendMode));
//        public static readonly GUIContent blendingMode = EditorGUIUtility.TrTextContent("ブレンドモード", "透明な表面の色が背景のマテリアルの色とどのようにブレンドされるかを制御します。");

//        public static readonly string[] renderFaceNames = Enum.GetNames(typeof(RenderFace));
//        public static readonly GUIContent cullingText = EditorGUIUtility.TrTextContent("Render Face", "ジオメトリからどの面をカリングするかを指定します。Front は前面をカリングします。Back は背面をカリングします。None は両側がレンダリングされることを意味します。");

//        public static readonly GUIContent gradientMapContent = new GUIContent("グラデーションマップ", "RGB カラー:A ");
//        public static readonly GUIContent gradientMapShadowContent = new GUIContent("グラデーションマップ影", "RGB カラー:A ");

//        public static readonly GUIContent albedoTexContent = new GUIContent("メインテクスチャ", "RGB カラー:A カットオフ");
//        public static readonly GUIContent emissionMapContent = new GUIContent("エミッションマップ", "RGB 色味調整:A 強度");
//        public static readonly GUIContent emissionNoiseTexContent = new GUIContent("エミッションノイズテクスチャ", "エミッシブにランダム性を出すためのマップ");
//        public static readonly GUIContent outlineWidthContent = new GUIContent("Outline Width", "Outline Width");
//        public static readonly GUIContent shadowTexContent = new GUIContent("影テクスチャ", "RGB カラー:A ボケ制御");
//        public static readonly GUIContent shadowRampTexContent = new GUIContent("陰影ランプテクスチャ", "RGB 肌色:A 影色");
//        public static readonly GUIContent packing1TexContent = new GUIContent("パッキング1テクスチャ", "R オクルージョン:G RimMask:B Metalic:A Smoothness");
//        public static readonly GUIContent packing2TexContent = new GUIContent("パッキング2テクスチャ", "R ランプ振り分け:G スペキュラマスク:B エミッシブマスク:A 手書きラインマスク");
//        public static readonly GUIContent rampTexContent = new GUIContent("Rampテクスチャ", "RGB カラー:A ");
//        public static readonly GUIContent rampMaskTexContent = new GUIContent("Rampマスクテクスチャ", "RGBCMKを1~6に割り振って使う");
//        public static readonly GUIContent matcapTexContent = new GUIContent("マットキャップテクスチャ", "RGB カラー:A ");
//        public static readonly GUIContent rimlightMaskTexContent = new GUIContent("リムライトマスク", "R マスク");
//        public static readonly GUIContent metallicTexContent = new GUIContent("メタリックテクスチャ", "金属表現用のテクスチャ");

//        public static readonly GUIContent sdfTexContent = new GUIContent("SDFテクスチャ", "");

//        public static readonly GUIContent angelRingHairLineTexContent = new GUIContent("髪テクスチャ", "RGB カラー:A ");
//        public static readonly GUIContent angelRingTexContent = new GUIContent("エンジェルリングテクスチャ", "RGB カラー:A ");
//        public static readonly GUIContent angelRingPackTexContent = new GUIContent("エンジェルリングパックテクスチャ", "");

//        public static readonly GUIContent wakameTexContent = new GUIContent("ワカメテクスチャ", "ハイライト用のテクスチャ");
//        public static readonly GUIContent wakameMaskTexContent = new GUIContent("ワカメマスクテクスチャ", "ハイライト用のマスクテクスチャ");
//    }

//    internal static void DrawFloatToggleProperty(GUIContent styles, MaterialProperty prop, int indentLevel = 0, bool isDisabled = false)
//    {
//        if (prop == null)
//            return;

//        EditorGUI.BeginDisabledGroup(isDisabled);
//        EditorGUI.indentLevel += indentLevel;
//        EditorGUI.BeginChangeCheck();
//        MaterialEditor.BeginProperty(prop);
//        bool newValue = EditorGUILayout.Toggle(styles, prop.floatValue == 1);
//        if (EditorGUI.EndChangeCheck())
//            prop.floatValue = newValue ? 1.0f : 0.0f;
//        MaterialEditor.EndProperty();
//        EditorGUI.indentLevel -= indentLevel;
//        EditorGUI.EndDisabledGroup();
//    }
//    static bool Foldout(bool display, string title)
//    {
//        var style = new GUIStyle("ShurikenModuleTitle");
//        style.font = new GUIStyle(EditorStyles.boldLabel).font;
//        style.border = new RectOffset(15, 7, 4, 4);
//        style.fixedHeight = 22;
//        style.contentOffset = new Vector2(20f, -2f);

//        var rect = GUILayoutUtility.GetRect(16f, 22f, style);
//        GUI.Box(rect, title, style);

//        var e = Event.current;

//        var toggleRect = new Rect(rect.x + 4f, rect.y + 2f, 13f, 13f);
//        if (e.type == EventType.Repaint)
//        {
//            EditorStyles.foldout.Draw(toggleRect, false, false, display, false);
//        }

//        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
//        {
//            display = !display;
//            e.Use();
//        }

//        return display;
//    }
//    static bool FoldoutSubMenu(bool display, string title)
//    {
//        var style = new GUIStyle("ShurikenModuleTitle");
//        style.font = new GUIStyle(EditorStyles.boldLabel).font;
//        style.border = new RectOffset(15, 7, 4, 4);
//        style.padding = new RectOffset(5, 7, 4, 4);
//        style.fixedHeight = 22;
//        style.contentOffset = new Vector2(32f, -2f);

//        var rect = GUILayoutUtility.GetRect(16f, 22f, style);
//        GUI.Box(rect, title, style);

//        var e = Event.current;

//        var toggleRect = new Rect(rect.x + 16f, rect.y + 2f, 13f, 13f);
//        if (e.type == EventType.Repaint)
//        {
//            EditorStyles.foldout.Draw(toggleRect, false, false, display, false);
//        }

//        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
//        {
//            display = !display;
//            e.Use();
//        }

//        return display;
//    }
//    public void FindProperties(MaterialProperty[] props)
//    {
//        surfaceTypeProp = FindProperty("_Surface", props, false);

//        blendModeProp = FindProperty("_Blend", props, false);
//        cullingProp = FindProperty("_Cull", props, false);
//        ztestProp = FindProperty("_ZTest", props, false);
//        alphaClipProp = FindProperty("_AlphaClip", props, false);
//        alphaCutoffProp = FindProperty("_Cutoff", props, false);

//        castShadowsProp = FindProperty("_CastShadows", props, false);
//        queueControlProp = FindProperty("_QueueControl", props, false);

//        albedoTex = FindProperty("_MainTex", props);
//        albedoColor = FindProperty("_MainColor", props);
//        alphaCutOff = FindProperty("_Cutoff", props);
//        alphaClipProp = FindProperty("_AlphaClip", props);

//        shadowTex = FindProperty("_ShadowMap", props);
//        shadowRampTex = FindProperty("_ShadowRampMap", props);
//        shadingArea = FindProperty("_ShadingArea", props);
//        shadingAreaGradation = FindProperty("_ShadingAreaGradation", props);
//        shadingAreaCorrection = FindProperty("_ShadingAreaCorrection", props);

//        shadowScale = FindProperty("_ShadowScale", props);

//        packing1Tex = FindProperty("_Packing1Tex", props);
//        packing2Tex = FindProperty("_Packing2Tex", props);
//        rampMaskTex = FindProperty("_RampMaskTex", props);
//        rampTex = FindProperty("_RampTex", props);
//        //rampSlider = FindProperty("_RampSlider", props);

//        //useMatCap = FindProperty("_Use_MatCap", props);
//        //matcapTex = FindProperty("_MatCapTex", props);
//        //matcapColor = FindProperty("_MatCapColor", props);
//        //matcapPawer = FindProperty("_MatCapPower", props);
//        //matcapMaskInfluence = FindProperty("_MatCapMaskInfluence", props);
//        //matcapBlendMode = FindProperty("_MatcapBlendMode", props);


//        //useRamp = FindProperty("_Use_Ramp", props);
//        //rampStep = FindProperty("_RampStep", props);

//        useOutline = FindProperty("_UseOutline", props);
//        useOutlineTex = FindProperty("_Use_OutlineTex", props);
//        outlineColorTex = FindProperty("_OutlineColorTex", props);

//        outlineWidth = FindProperty("_OutlineWidth", props);
//        debugOutlineWidth = FindProperty("_DebugOutlineWidth", props);
//        outlineColor = FindProperty("_OutlineColor", props);

//        pointLight = FindProperty("_PointLight", props);

//        stencilNo = FindProperty("_StencilNo", props);
//    }
//    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props)
//    {
//        FindProperties(props);

//        EditorGUIUtility.fieldWidth = 0;

//        Material material = materialEditor.target as Material;

//        _BaseSettings_Foldout = Foldout(_BaseSettings_Foldout, "【基本設定】");
//        if (_BaseSettings_Foldout)
//        {
//            EditorGUI.indentLevel++;
//            EditorGUILayout.Space();
//            using (new EditorGUILayout.VerticalScope("HelpBox"))
//            {
//                GUI_BaseSettings(materialEditor, material);
//            }
//            EditorGUI.indentLevel--;
//        }
//        EditorGUILayout.Space();
//        _Outline_Foldout = Foldout(_Outline_Foldout, "【アウトライン設定】");
//        if (_Outline_Foldout)
//        {
//            EditorGUI.indentLevel++;
//            EditorGUILayout.Space();
//            using (new EditorGUILayout.VerticalScope("HelpBox"))
//            {
//                GUI_Outline(materialEditor, material);
//            }
//            EditorGUI.indentLevel--;
//        }
//        EditorGUILayout.Space();

//        _RenderingSettings_Foldout = Foldout(_RenderingSettings_Foldout, "【描画設定】");
//        if (_RenderingSettings_Foldout)
//        {
//            EditorGUI.indentLevel++;
//            EditorGUILayout.Space();
//            using (new EditorGUILayout.VerticalScope("HelpBox"))
//            {
//                GUI_RenderingSettings(materialEditor, material);
//            }
//            EditorGUI.indentLevel--;
//        }
//    }
//    void GUI_BaseSettings(MaterialEditor materialEditor, Material material)
//    {
//        GUI_All(materialEditor, material);
//    }
//    void GUI_All(MaterialEditor materialEditor, Material material)
//    {
//        EditorGUILayout.BeginHorizontal();
//        materialEditor.TexturePropertySingleLine(Styles.albedoTexContent, albedoTex);
//        materialEditor.ColorProperty(albedoColor, "");
//        EditorGUILayout.EndHorizontal();
//        materialEditor.RangeProperty(alphaCutOff, "アルファカットオフ");

//        EditorGUI.DrawRect(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 1)), new Color(0.31f, 0.31f, 0.31f));
//        materialEditor.TexturePropertySingleLine(Styles.shadowTexContent, shadowTex);
//        materialEditor.TexturePropertySingleLine(Styles.shadowRampTexContent, shadowRampTex);

//        materialEditor.ShaderProperty(shadowScale, "影サイズ");

//        materialEditor.RangeProperty(shadingArea, "シェーディング強度");
//        materialEditor.RangeProperty(shadingAreaGradation, "ぼかし強度");
//        materialEditor.ShaderProperty(shadingAreaCorrection, "影調整");


//        EditorGUI.DrawRect(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 1)), new Color(0.31f, 0.31f, 0.31f));

//        materialEditor.TexturePropertySingleLine(Styles.packing1TexContent, packing1Tex);
//        materialEditor.TexturePropertySingleLine(Styles.packing2TexContent, packing2Tex);
//        materialEditor.TexturePropertySingleLine(Styles.rampTexContent, rampTex);
//        materialEditor.TexturePropertySingleLine(Styles.rampMaskTexContent, rampMaskTex);


//        //materialEditor.ShaderProperty(drawLineColor, "描きライン色");
//        //materialEditor.ShaderProperty(drawLineSmoothMin, "描きラインスムース下限");
//        //materialEditor.ShaderProperty(drawLineSmoothMax, "描きラインスムース上限");

//        //EditorGUI.DrawRect(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 1)), new Color(0.31f, 0.31f, 0.31f));


//        //materialEditor.TexturePropertySingleLine(Styles.metallicTexContent, matallicTex);
//        //materialEditor.ShaderProperty(matallicBlendMode, "メタリックブレンドモード");

//        //materialEditor.ShaderProperty(specularColor, "スペキュラーカラー");
//        //materialEditor.ShaderProperty(specularPower, "スペキュラーパワー");
//        //EditorGUI.DrawRect(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 1)), new Color(0.31f, 0.31f, 0.31f));

//        //materialEditor.ShaderProperty(useDecalProp, "Receive Decal");
//        //EditorGUI.DrawRect(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 1)), new Color(0.31f, 0.31f, 0.31f));

//        //materialEditor.ShaderProperty(useMatCap, "マットキャップ使用");
//        //if (material.GetFloat("_Use_MatCap") == 1)
//        //{
//        //    materialEditor.ShaderProperty(matcapBlendMode, "BlendMode");
//        //    EditorGUILayout.BeginHorizontal();
//        //    materialEditor.TexturePropertySingleLine(Styles.matcapTexContent, matcapTex);
//        //    materialEditor.ColorProperty(matcapColor, "");
//        //    materialEditor.ShaderProperty(matcapPawer, "強度");
//        //    EditorGUILayout.EndHorizontal();
//        //    materialEditor.ShaderProperty(matcapMaskInfluence, "マスク影響度");
//        //}

//        EditorGUI.DrawRect(EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(false, 1)), new Color(0.31f, 0.31f, 0.31f));

//        materialEditor.ShaderProperty(stencilNo, "ステンシル番号");
//    }
//    internal static void SetMaterialSrcDstBlendProperties(Material material, UnityEngine.Rendering.BlendMode srcBlend, UnityEngine.Rendering.BlendMode dstBlend)
//    {
//        if (material.HasProperty("_SrcBlend"))
//            material.SetFloat("_SrcBlend", (float)srcBlend);

//        if (material.HasProperty("_DstBlend"))
//            material.SetFloat("_DstBlend", (float)dstBlend);

//        if (material.HasProperty("_SrcBlendAlpha"))
//            material.SetFloat("_SrcBlendAlpha", (float)srcBlend);

//        if (material.HasProperty("_DstBlendAlpha"))
//            material.SetFloat("_DstBlendAlpha", (float)dstBlend);
//    }
//    internal static void SetMaterialSrcDstBlendProperties(Material material, UnityEngine.Rendering.BlendMode srcBlendRGB, UnityEngine.Rendering.BlendMode dstBlendRGB, UnityEngine.Rendering.BlendMode srcBlendAlpha, UnityEngine.Rendering.BlendMode dstBlendAlpha)
//    {
//        if (material.HasProperty("_SrcBlend"))
//            material.SetFloat("_SrcBlend", (float)srcBlendRGB);

//        if (material.HasProperty("_DstBlend"))
//            material.SetFloat("_DstBlend", (float)dstBlendRGB);

//        if (material.HasProperty("_SrcBlendAlpha"))
//            material.SetFloat("_SrcBlendAlpha", (float)srcBlendAlpha);

//        if (material.HasProperty("_DstBlendAlpha"))
//            material.SetFloat("_DstBlendAlpha", (float)dstBlendAlpha);
//    }
//    internal static void SetMaterialZWriteProperty(Material material, bool zwriteEnabled)
//    {
//        if (material.HasProperty("_ZWrite"))
//            material.SetFloat("_ZWrite", zwriteEnabled ? 1.0f : 0.0f);
//    }
//    internal static void SetupMaterialBlendModeInternal(Material material, out int automaticRenderQueue)
//    {
//        if (material == null)
//            throw new ArgumentNullException("material");

//        bool alphaClip = false;
//        if (material.HasProperty("_AlphaClip"))
//            alphaClip = material.GetFloat("_AlphaClip") >= 0.5;
//        CoreUtils.SetKeyword(material, "_ALPHATEST_ON", alphaClip);

//        // default is to use the shader render queue
//        int renderQueue = material.shader.renderQueue;
//        material.SetOverrideTag("RenderType", "");      // clear override tag
//        if (material.HasProperty("_Surface"))
//        {
//            SurfaceType surfaceType = (SurfaceType)material.GetFloat("_Surface");
//            bool zwrite = false;
//            CoreUtils.SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", surfaceType == SurfaceType.Transparent);
//            bool alphaToMask = false;
//            if (surfaceType == SurfaceType.Opaque)
//            {
//                if (alphaClip)
//                {
//                    renderQueue = (int)RenderQueue.AlphaTest;
//                    material.SetOverrideTag("RenderType", "TransparentCutout");
//                    alphaToMask = true;
//                }
//                else
//                {
//                    renderQueue = (int)RenderQueue.Geometry;
//                    material.SetOverrideTag("RenderType", "Opaque");
//                }

//                SetMaterialSrcDstBlendProperties(material, UnityEngine.Rendering.BlendMode.One, UnityEngine.Rendering.BlendMode.Zero);
//                zwrite = true;
//                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
//                material.DisableKeyword("_ALPHAMODULATE_ON");
//            }
//            else // SurfaceType Transparent
//            {
//                BlendMode blendMode = (BlendMode)material.GetFloat("_Blend");

//                var srcBlendRGB = UnityEngine.Rendering.BlendMode.One;
//                var dstBlendRGB = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
//                var srcBlendA = UnityEngine.Rendering.BlendMode.One;
//                var dstBlendA = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;

//                switch (blendMode)
//                {
//                    case BlendMode.Alpha:
//                        srcBlendRGB = UnityEngine.Rendering.BlendMode.SrcAlpha;
//                        dstBlendRGB = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
//                        srcBlendA = UnityEngine.Rendering.BlendMode.One;
//                        dstBlendA = dstBlendRGB;
//                        //Debug.Log("aaa");
//                        break;
//                    case BlendMode.Premultiply:
//                        srcBlendRGB = UnityEngine.Rendering.BlendMode.One;
//                        dstBlendRGB = UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha;
//                        srcBlendA = srcBlendRGB;
//                        dstBlendA = dstBlendRGB;
//                        break;
//                    case BlendMode.Additive:
//                        srcBlendRGB = UnityEngine.Rendering.BlendMode.SrcAlpha;
//                        dstBlendRGB = UnityEngine.Rendering.BlendMode.One;
//                        srcBlendA = UnityEngine.Rendering.BlendMode.One;
//                        dstBlendA = dstBlendRGB;
//                        break;
//                    case BlendMode.Multiply:
//                        srcBlendRGB = UnityEngine.Rendering.BlendMode.DstColor;
//                        dstBlendRGB = UnityEngine.Rendering.BlendMode.Zero;
//                        srcBlendA = UnityEngine.Rendering.BlendMode.Zero;
//                        dstBlendA = UnityEngine.Rendering.BlendMode.One;
//                        break;
//                }
//                bool offScreenAccumulateAlpha = false;
//                if (offScreenAccumulateAlpha)
//                    srcBlendA = UnityEngine.Rendering.BlendMode.Zero;

//                SetMaterialSrcDstBlendProperties(material, srcBlendRGB, dstBlendRGB, // RGB
//                    srcBlendA, dstBlendA); // Alpha

//                CoreUtils.SetKeyword(material, "_ALPHAMODULATE_ON", blendMode == BlendMode.Multiply);

//                material.SetOverrideTag("RenderType", "Transparent");
//                zwrite = false;
//                renderQueue = (int)RenderQueue.Transparent;
//            }

//            if (material.HasProperty("_AlphaToMask"))
//            {
//                material.SetFloat("_AlphaToMask", alphaToMask ? 1.0f : 0.0f);
//            }

//            SetMaterialZWriteProperty(material, zwrite);
//            material.SetShaderPassEnabled("DepthOnly", zwrite);
//        }
//        else
//        {
//            material.SetShaderPassEnabled("DepthOnly", true);
//        }
//        if (material.HasProperty("_QueueControl"))
//            renderQueue += (int)material.GetFloat("_QueueControl");

//        automaticRenderQueue = renderQueue;
//    }
//    void GUI_Outline(MaterialEditor materialEditor, Material material)
//    {
//        var srpDefaultLightModeTag = material.GetTag("LightMode", false, srpDefaultLightModeName);
//        bool isOutlineEnabled = true;
//        if (srpDefaultLightModeTag == srpDefaultLightModeName)
//        {
//            EditorGUILayout.BeginHorizontal();
//            EditorGUILayout.PrefixLabel("有効化");
//            if (isOutlineEnabled = material.GetShaderPassEnabled(srpDefaultLightModeName))
//            {
//                if (GUILayout.Button(STR_ONSTATE, shortButtonStyle))
//                {
//                    material.SetShaderPassEnabled(srpDefaultLightModeName, false);
//                    material.SetShaderPassEnabled(srpDepthLightModeName, false);
//                    material.SetShaderPassEnabled(srpDepthNormalLightModeName, false);
//                }
//            }
//            else
//            {
//                if (GUILayout.Button(STR_OFFSTATE, shortButtonStyle))
//                {
//                    material.SetShaderPassEnabled(srpDefaultLightModeName, true);
//                    material.SetShaderPassEnabled(srpDepthLightModeName, true);
//                    material.SetShaderPassEnabled(srpDepthNormalLightModeName, true);
//                }
//            }
//            EditorGUILayout.EndHorizontal();
//        }
//        if (!isOutlineEnabled)
//        {
//            return;
//        }
//        materialEditor.ShaderProperty(useOutlineTex, "カラーテクスチャ使用");
//        if (material.GetFloat("_Use_OutlineTex") == 1)
//        {
//            materialEditor.ShaderProperty(outlineColorTex, "");
//        }
//        materialEditor.RangeProperty(outlineWidth, "太さ");
//        //materialEditor.RangeProperty(debugOutlineWidth, "仮太さ調整");
//        materialEditor.ColorProperty(outlineColor, "色");
//    }
//    void GUI_RenderingSettings(MaterialEditor materialEditor, Material material)
//    {
//        materialEditor.PopupShaderProperty(surfaceTypeProp, Styles.surfaceType, Styles.surfaceTypeNames);
//        if ((surfaceTypeProp != null) && ((SurfaceType)surfaceTypeProp.floatValue == SurfaceType.Transparent))
//        {
//            materialEditor.PopupShaderProperty(blendModeProp, Styles.blendingMode, Styles.blendModeNames);
//        }
//        materialEditor.PopupShaderProperty(cullingProp, Styles.cullingText, Styles.renderFaceNames);

//        SetupMaterialBlendModeInternal(material, out int automaticRenderQueue);
//        DrawFloatToggleProperty(Styles.alphaClipText, alphaClipProp);
//        if (SupportedRenderingFeatures.active.editableMaterialRenderQueue)
//        {
//            materialEditor.RenderQueueField();
//        }
//        materialEditor.EnableInstancingField();
//        materialEditor.DoubleSidedGIField();
//    }
//}
