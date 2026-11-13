using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

// PunishingCloth 专用材质面板：只负责参数显示与关键字同步。
// 使用 MaterialEditor 的原生控件，保留撤销、多材质编辑和贴图缩放/偏移。
public sealed class PunishingClothShaderGUI : ShaderGUI
{
    private MaterialEditor editor;
    private MaterialProperty[] properties;

    // 折叠状态保存在当前 Inspector 中，不会写入材质。
    private bool texturesOpen = true, shadowOpen = true, aoOpen = true;
    private bool pbrOpen = true, ggxOpen = true, nprOpen;
    private bool outlineOpen = true, debugOpen = true, settingsOpen;

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] materialProperties)
    {
        editor = materialEditor;
        properties = materialProperties;
        EditorGUI.BeginChangeCheck();

        // ---------- 贴图：每张贴图保留独立的缩放和偏移 ----------
        if (Section("纹理与法线", ref texturesOpen))
        {
            Texture("_BaseMap");
            Texture("_AOMap");
            Texture("_NormalMap", "_NormalScale");
            Texture("_PBRMap");
        }

        Group("NPR / PBR 共用二分阴影", ref shadowOpen,
            "_ShadowColor", "_ShadowThreshold", "_ShadowSoftness");
        Group("AOMap.G 固有阴影", ref aoOpen,
            "_ShadowAOBlackPoint", "_ShadowAOWhitePoint", "_ShadowAOStrength");
        Group("PBR 材质与直接漫反射", ref pbrOpen,
            "_MetallicScale", "_MetallicOffset", "_RoughnessScale", "_RoughnessOffset", "_PBRDiffuseToon");

        // ---------- 高光：开关关闭时保留参数，禁用编辑以明确当前状态 ----------
        if (Section("GGX D 项风格化高光", ref ggxOpen))
        {
            Property("_PBRSpecularStylized");
            using (new EditorGUI.DisabledScope(IsOff("_PBRSpecularStylized")))
            {
                EditorGUILayout.LabelField("第一层亮斑", EditorStyles.boldLabel);
                Property("_GGXFirstThreshold");
                Property("_GGXFirstSoftness");
                Property("_GGXFirstIntensity");
                Property("_GGXFirstStyle");

                EditorGUILayout.LabelField("第二层亮斑", EditorStyles.boldLabel);
                Property("_GGXSecondThreshold");
                Property("_GGXSecondSoftness");
                Property("_GGXSecondIntensity");
                Property("_GGXSecondStyle");
                Property("_GGXLayerBlend");
            }
        }

        if (Section("NPR 高光（当前停用）", ref nprOpen))
        {
            EditorGUILayout.HelpBox("当前 Shader 中的 NPR 高光计算已注释，以下参数暂不影响画面。", MessageType.Info);
            using (new EditorGUI.DisabledScope(true))
            {
                Property("_NPRSpecColor");
                Property("_NPRSpecIntensity");
                Property("_NPRSpecExponent");
                Property("_NPRSpecThreshold");
                Property("_NPRSpecSoftness");
            }
        }

        // ---------- 描边：五种计算方式集中在一个下拉框里 ----------
        if (Section("描边", ref outlineOpen))
        {
            bool mixed = DrawOutlineMode();
            Property("_OutlineColor");
            MaterialProperty mode = FindProperty("_OutlineMode", properties);
            MaterialProperty projection = FindProperty("_OutlineProjectionMode", properties);
            bool screen = mode.floatValue >= 0.5f && mode.floatValue < 1.5f;
            bool perspective = screen
                && projection.floatValue >= 0.5f && projection.floatValue < 1.5f;
            MaterialProperty depthScaling = FindProperty("_OutlineDepthScaling", properties);
            MaterialProperty widthScalingMode = FindProperty("_OutlineWidthScalingMode", properties);
            bool boundsDriven = !mixed && perspective && !depthScaling.hasMixedValue
                && depthScaling.floatValue > 0.5f && !widthScalingMode.hasMixedValue
                && widthScalingMode.floatValue > 0.5f;

            // 原版保留自己的宽度参数；透视校正版沿用单独的像素宽度。
            // 多选不同模式的材质时，两种宽度都允许编辑。
            using (new EditorGUI.DisabledScope(!mixed && perspective))
            {
                if (boundsDriven)
                {
                    // 原脚本更新 _OutlineWidth；只读显示，不用滑块限制脚本传入的数值。
                    MaterialProperty boundsWidth = FindProperty("_OutlineWidth", properties);
                    EditorGUI.showMixedValue = boundsWidth.hasMixedValue;
                    EditorGUILayout.FloatField("Bounds 传入线宽（像素）", boundsWidth.floatValue);
                    EditorGUI.showMixedValue = false;
                }
                else
                {
                    Property("_OutlineWidth");
                }
            }
            using (new EditorGUI.DisabledScope((!mixed && !perspective) || boundsDriven))
                Property("_OutlineWidthPixels");

            // 只有全部所选材质都使用“像素宽度（透视校正）”时，才显示这个勾选项。
            if (!mixed && perspective)
            {
                Property("_OutlineDepthScaling");
                if (!IsOff("_OutlineDepthScaling"))
                {
                    editor.PopupShaderProperty(widthScalingMode, new GUIContent("线宽变化方式"),
                        new[] { "相机观察深度", "Bounds 屏幕占比（C#）" });

                    if (!widthScalingMode.hasMixedValue && widthScalingMode.floatValue >= 0.5f)
                    {
                        EditorGUILayout.HelpBox(
                            "原 BoundsSetOutlineWidth 脚本每帧写入 _OutlineWidth，上方只读显示。\n" +
                            "基础线宽和参考屏幕高度占比在脚本组件上调整。", MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "按相机观察深度补偿到 1 个世界单位，超过后描边随距离变细。", MessageType.Info);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("保持固定像素宽度。", MessageType.Info);
                }
            }

            EditorGUILayout.HelpBox(
                "世界空间 / View：使用原版宽度，具体倍率见对应算法。\n" +
                "屏幕比例：原版宽度 × 0.01 个 NDC 单位。\n" +
                "原版像素：原版宽度直接表示像素数。\n" +
                "透视校正：可使用固定像素宽度、相机深度或 Bounds 屏幕占比。", MessageType.Info);
        }

        // ---------- 通道调试：贴图或顶点色共用 RGBA 选择 ----------
        if (Section("通道调试", ref debugOpen))
        {
            Property("_DebugTextureEnabled");
            using (new EditorGUI.DisabledScope(IsOff("_DebugTextureEnabled")))
            {
                Property("_DebugSource");
                MaterialProperty source = FindProperty("_DebugSource", properties);
                using (new EditorGUI.DisabledScope(!source.hasMixedValue && source.floatValue >= 0.5f))
                    Texture("_DebugMap");
                Property("_PreviewChannel");
            }
        }

        if (Section("材质设置", ref settingsOpen))
        {
            editor.RenderQueueField();
            editor.EnableInstancingField();
            editor.DoubleSidedGIField();
        }

        // 使用现有 Toggle 对应的关键字，不额外创建描边模式关键字。
        if (EditorGUI.EndChangeCheck())
        {
            foreach (Object selected in editor.targets)
                ValidateMaterial((Material)selected);
            editor.PropertiesChanged();
        }
    }

    // 面板显示五项，底层继续使用已有的两个参数，保留材质当前的选择。
    private bool DrawOutlineMode()
    {
        MaterialProperty mode = FindProperty("_OutlineMode", properties);
        MaterialProperty projection = FindProperty("_OutlineProjectionMode", properties);
        bool screen = mode.floatValue >= 0.5f && mode.floatValue < 1.5f;
        bool mixed = mode.hasMixedValue || (screen && projection.hasMixedValue);

        // 面板顺序：世界空间、原版比例、原版像素、透视校正、View 空间。
        int selection;
        if (mode.floatValue < 0.5f)
            selection = 0;
        else if (mode.floatValue >= 1.5f)
            selection = 4;
        else if (projection.floatValue < 0.5f)
            selection = 2;
        else if (projection.floatValue < 1.5f)
            selection = 3;
        else
            selection = 1;

        MaterialEditor.BeginProperty(mode);
        MaterialEditor.BeginProperty(projection);
        EditorGUI.showMixedValue = mixed;
        EditorGUI.BeginChangeCheck();
        selection = EditorGUILayout.Popup("描边计算方式", selection, new[]
        {
            "世界空间（原版）", "屏幕比例（原版）", "像素宽度（原版）", "像素宽度（透视校正）", "观察空间（View）"
        });
        bool changed = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;
        if (changed)
        {
            editor.RegisterPropertyChangeUndo("切换描边计算方式");
            if (selection == 0)
            {
                mode.floatValue = 0f;
            }
            else if (selection == 4)
            {
                mode.floatValue = 2f;
            }
            else
            {
                mode.floatValue = 1f;
                projection.floatValue = selection == 1 ? 2f : selection == 2 ? 0f : 1f;
            }
        }
        MaterialEditor.EndProperty();
        MaterialEditor.EndProperty();
        return mixed && !changed;
    }

    // 材质加载、验证和面板编辑时，让数值开关与本地关键字保持一致。
    public override void ValidateMaterial(Material material)
    {
        SyncKeyword(material, "_DebugTextureEnabled", "_DEBUG_TEXTURE");
        SyncKeyword(material, "_PBRSpecularStylized", "_PBR_SPECULAR_STYLIZED");
    }

    private static void SyncKeyword(Material material, string propertyName, string keyword)
    {
        if (!material.HasProperty(propertyName))
            return;
        if (material.GetFloat(propertyName) > 0.5f)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    // ---------- 复用的小控件：集中查找参数，避免各区域重复写绘制代码 ----------
    private static bool Section(string title, ref bool expanded)
    {
        EditorGUILayout.Space(5f);
        expanded = EditorGUILayout.Foldout(expanded, title, true, EditorStyles.foldoutHeader);
        return expanded;
    }

    private void Group(string title, ref bool expanded, params string[] names)
    {
        if (!Section(title, ref expanded))
            return;
        foreach (string name in names)
            Property(name);
    }

    private void Property(string name)
    {
        MaterialProperty property = FindProperty(name, properties);
        editor.ShaderProperty(property, property.displayName);
    }

    private void Texture(string name, string extraName = null)
    {
        MaterialProperty texture = FindProperty(name, properties);
        MaterialProperty extra = extraName == null ? null : FindProperty(extraName, properties);
        editor.TexturePropertySingleLine(new GUIContent(texture.displayName), texture, extra);
        editor.TextureScaleOffsetProperty(texture);
    }

    private bool IsOff(string name)
    {
        MaterialProperty property = FindProperty(name, properties);
        return !property.hasMixedValue && property.floatValue < 0.5f;
    }
}
