Shader "CartoonShading/PunishingBody"
{
    Properties
    {
        // ==================== 纹理输入 ====================
        // 四张贴图分别保留独立的缩放与偏移，通道用途在片元函数中处理。
        [MainTexture] _BaseMap("基础色贴图 (Base Color)", 2D) = "white" {}
        _AOMap("遮罩贴图 (AO Map)", 2D) = "white" {}
        [Normal] _NormalMap("法线贴图 (Normal Map)", 2D) = "bump" {}
        // 皮肤颜色 Ramp：按片元函数中的明暗权重采样。
        _BodySkinRampMap("身体皮肤 Ramp (Body Skin Ramp)", 2D) = "white" {}

        // ==================== 皮肤渲染方式 ====================
        // 0 连续 Ramp；1 二分 Ramp 叠加阴影色；2 二分阴影叠加假 SSS 交界色带。
        [Enum(Ramp, 0, ToonRamp, 1, FakeSSS, 2)] _SkinShadingMode("皮肤渲染方式", Float) = 1
        // 在白色与 Ramp 颜色之间混合：0 不改变基础色，1 完整使用 Ramp 乘色。
        _RampTintStrength("Ramp 染色强度", Range(0, 1)) = 0.3

        // ==================== 法线控制 ====================
        // 控制切线空间法线的凹凸强度；设为 0 时使用模型自身法线。
        _NormalScale("法线强度", Range(0, 2)) = 1

        // ==================== 身体二分阴影 ====================
        // 方式 1 和 2 使用；在暗部颜色与白色之间插值，保留基础色细节。
        _ShadowColor("暗部颜色", Color) = (0.5, 0.5, 0.65, 1)
        // 当前输入为半兰伯特值，分界越大，暗部覆盖范围越大。
        _ShadowThreshold("明暗分界阈值", Range(-1, 1)) = 0
        // 阈值两侧的过渡半宽，越接近 0，二分边界越硬。
        _ShadowSoftness("明暗过渡半宽", Range(0, 1)) = 0.05
        // 缩放 fwidth(NdotL) 提供的抗锯齿宽度，默认沿用原来的 0.5 倍。
        _ShadowAAFactor("阴影边缘抗锯齿倍率", Range(0, 2)) = 0.5

        // ==================== 假 SSS 交界色带 ====================
        // 仅方式 2 使用：在明暗过渡区加色，强度设为 0 即关闭色带。
        _ShadowEdgeColor("假 SSS 交界颜色", Color) = (0.3, 0, 0, 1)
        _ShadowEdgeStrength("假 SSS 交界强度", Range(0, 2)) = 1

        // ==================== G 通道固有阴影 ====================
        // AOMap.g 的通道值需按贴图实际范围校正，不能直接当作白色 1 使用。
        // 先将黑点到白点之间的范围平滑映射为 0~1，避免非遮蔽区域整体变暗。
        _ShadowAOBlackPoint("固有阴影黑点", Range(0, 1)) = 0.02
        _ShadowAOWhitePoint("固有阴影白点", Range(0, 1)) = 0.33
        // 三种方式共用 AO：0 完全忽略固有阴影，1 完全使用重映射结果。
        _ShadowAOStrength("固有阴影强度", Range(0, 1)) = 1

        // ==================== 描边渲染 ====================
        // 0：世界空间；1：屏幕空间；2：观察空间。保留已有材质的 0/1 含义。
        // ShaderGUI 将此参数与屏幕算法参数合成五种对比选项。
        [Enum(vertWSOutline, 0, vertCSOutline, 1, vertVSOutline, 2)] _OutlineMode("描边外扩模式", Float) = 1
        _OutlineColor("描边颜色", Color) = (0, 0, 0, 1)
        // 与烘焙窗口一致：0 RGB，1 八面体，2 半八面体；默认保留当前的半八面体解码。
        [Enum(RGB, 0, Oct, 1, HemiOct, 2)] _OutlineNormalEncoding("描边法线编码", Float) = 2
        // 保留原先的裁剪空间 z 偏移，只在透视校正描边模式中使用。
        _OutlineZOffset("描边 Z 偏移（裁剪空间）", Float) = 0.0008
        // 三种原版和观察空间版沿用原有宽度参数，保留各自公式中的系数。
        _OutlineWidth("原版描边宽度", Range(0, 6)) = 0.01
        _OutlineWidthPixels("透视校正宽度（像素）", Range(0, 10)) = 2
        // 仅供透视校正模式使用。关闭保持固定像素宽度，开启后选择变化方式。
        [ToggleUI] _OutlineDepthScaling("根据深度缩放宽度", Float) = 0
        [Enum(CameraDepth, 0, BoundsScreenRatio, 1, HeightRatio, 2)] _OutlineWidthScalingMode("线宽变化方式", Float) = 0
        // 仅用于人物高度占比估算；按世界空间高度填写，默认沿用原来的 1.6。
        _OutlineCharacterHeight("人物高度（世界单位）", Float) = 1.6
        // BoundsSetOutlineWidth 沿用原脚本，直接向上方 _OutlineWidth 写入像素线宽。
        // 保留旧值：0 原版像素，1 透视校正；新增 2 原版屏幕比例。
        [Enum(OriginalPixels, 0, PerspectiveCorrect, 1, OriginalRatio, 2)] _OutlineProjectionMode("屏幕描边计算方式", Float) = 1

        // ==================== 贴图 / 顶点色通道调试 ====================
        // 沿用原有关键字开关；关闭时整个调试分支不参与正常渲染。
        [Toggle(_DEBUG_TEXTURE)] _DebugTextureEnabled("启用通道调试", Float) = 0
        // Texture 查看下方贴图，VertexColor 查看从模型 COLOR 属性传入的顶点色。
        [Enum(Texture, 0, VertexColor, 1)] _DebugSource("调试来源 (Texture 贴图 / VertexColor 顶点色)", Float) = 0
        _DebugMap("调试贴图", 2D) = "white" {}
        // RGB 显示采样颜色；单通道复制到 RGB 显示灰度，输出始终不透明。
        [Enum(RGB, 0, R, 1, G, 2, B, 3, A, 4)] _PreviewChannel("调试显示通道", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        // 所有 Pass 共用同一份材质常量，保持 UnityPerMaterial 布局一致。
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ==================== 材质常量 ====================
            // 材质参数集中在同一个常量缓冲区，以兼容 SRP Batcher。
            CBUFFER_START(UnityPerMaterial)
                // 各贴图的缩放与偏移。
                float4 _BaseMap_ST;
                float4 _AOMap_ST;
                float4 _NormalMap_ST;
                float4 _BodySkinRampMap_ST;
                float4 _DebugMap_ST;
                float _NormalScale;
                // 使用普通 if 切换三种方式，无需着色器关键字。
                float _RampTintStrength;
                float _SkinShadingMode;
                // 身体二分阴影的颜色、分界与过渡参数。
                half4 _ShadowColor;
                half4 _ShadowEdgeColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
                float _ShadowAAFactor;
                float _ShadowEdgeStrength;
                // 固有阴影的通道范围校正与强度控制。
                float _ShadowAOBlackPoint;
                float _ShadowAOWhitePoint;
                float _ShadowAOStrength;
                // 调试属性与关键字开关对应，保留在材质常量中供 Inspector 编辑。
                float _DebugTextureEnabled;
                float _DebugSource;
                float _PreviewChannel;
                // 描边材质参数同样集中声明，后续启用描边 Pass 时直接复用。
                half4 _OutlineColor;
                float _OutlineNormalEncoding;
                float _OutlineZOffset;
                float _OutlineWidth;
                float _OutlineMode;
                float _OutlineWidthPixels;
                float _OutlineProjectionMode;
                float _OutlineDepthScaling;
                float _OutlineWidthScalingMode;
                float _OutlineCharacterHeight;
            CBUFFER_END
        ENDHLSL

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            // 将调试采样限制在开启关键字的片元变体中。
            #pragma shader_feature_local_fragment _DEBUG_TEXTURE

            // ==================== 管线依赖 ====================
            // Core 已在共用 HLSLINCLUDE 中引入；Lighting 提供 URP 的光照计算。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ==================== 顶点输入与插值数据 ====================
            // 模型切线的 w 分量记录基底朝向，用于处理镜像 UV。
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 vertexColor : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 vertexColor : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float3 positionWS : TEXCOORD5;
            };

            // ==================== 纹理与采样器 ====================
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AOMap);
            SAMPLER(sampler_AOMap);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_BodySkinRampMap);
            SAMPLER(sampler_BodySkinRampMap);
            TEXTURE2D(_DebugMap);
            SAMPLER(sampler_DebugMap);


            // ==================== 顶点阶段 ====================
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                // 转换顶点位置，传递原始 UV 与顶点色，供片元阶段采样和扩展。
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = positionInputs.positionCS;
                // 世界空间位置用于逐像素计算观察方向与环境反射。
                OUT.positionWS = positionInputs.positionWS;
                OUT.uv = IN.uv;
                OUT.vertexColor = IN.vertexColor;
                // 在顶点阶段准备世界空间 TBN 基底，减少片元阶段的重复计算。
                // URP 内部已处理切线朝向与物体负缩放，不再额外重复翻转。
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.normalWS = normalInputs.normalWS;
                OUT.tangentWS = normalInputs.tangentWS;
                OUT.bitangentWS = normalInputs.bitangentWS;
                return OUT;
            }

            // ==================== 片元阶段 ====================
            half4 frag(Varyings IN) : SV_Target
            {
                // ---------- 调试输出：查看贴图或模型顶点色 ----------
                // 提前返回，避免主光、AO 和 Ramp 染色改变通道观察结果。
                #if defined(_DEBUG_TEXTURE)
                    // 顶点色已由顶点阶段传入并在三角形内部插值，可直接读取。
                    half4 debugValue = IN.vertexColor;
                    if (_DebugSource < 0.5)
                        debugValue = SAMPLE_TEXTURE2D(_DebugMap, sampler_DebugMap, TRANSFORM_TEX(IN.uv, _DebugMap));

                    // 两种来源共用同一组通道选项；Alpha 也以不透明灰度显示。
                    if (_PreviewChannel == 1) return half4(debugValue.rrr, 1);
                    if (_PreviewChannel == 2) return half4(debugValue.ggg, 1);
                    if (_PreviewChannel == 3) return half4(debugValue.bbb, 1);
                    if (_PreviewChannel == 4) return half4(debugValue.aaa, 1);
                    return half4(debugValue.rgb, 1);
                #endif

                // ---------- 1. 纹理采样 ----------
                // 保留完整 RGBA，方便后续逐通道定义遮罩与材质用途。
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(IN.uv, _BaseMap));
                half4 aoMap = SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, TRANSFORM_TEX(IN.uv, _AOMap));
                half4 normalMap = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, TRANSFORM_TEX(IN.uv, _NormalMap));
                
                // ---------- 2. 逐像素法线 ----------
                // 使用插值后的 TBN 转换贴图法线；最终归一化需保留在片元阶段。
                half3 normalTS = UnpackNormalScale(normalMap, _NormalScale);
                float3x3 tangentToWorld = float3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                Light mainLight = GetMainLight();
                float NdotL = dot(normalWS, mainLight.direction) * 0.5 + 0.5;              
                float aaWidth = fwidth(NdotL) * _ShadowAAFactor;
                float softness = max(_ShadowSoftness,aaWidth);
                half3 rampTint = half3(1, 1, 1);
                float shadowAOStart = min(_ShadowAOBlackPoint, _ShadowAOWhitePoint);
                float shadowAOEnd = max(max(_ShadowAOBlackPoint, _ShadowAOWhitePoint),shadowAOStart + 0.0001);
                float remappedAO = smoothstep(shadowAOStart, shadowAOEnd, aoMap.g);
                float shadowAO = saturate(lerp(1.0, remappedAO, _ShadowAOStrength));
                // ---------- 3. 皮肤渲染：三种方式独立比较 ----------
                if (_SkinShadingMode < 0.5)
                {
                    // 方式 1：半兰伯特直接采样 Ramp，AO 直接乘到染色结果上。
                    half4 rampMap = SAMPLE_TEXTURE2D(_BodySkinRampMap, sampler_BodySkinRampMap, TRANSFORM_TEX(half2(NdotL, 0.5), _BodySkinRampMap));
                    rampTint = lerp(half3(1, 1, 1), rampMap.rgb, _RampTintStrength) * shadowAO;
                }
                else if (_SkinShadingMode < 1.5)
                {
                    // 方式 2：保留二分 Ramp、阴影乘色以及直接乘 AO 的原有算法。                
                    float lightBand = smoothstep(_ShadowThreshold - softness, _ShadowThreshold + softness, NdotL);
                    half4 rampMap = SAMPLE_TEXTURE2D(_BodySkinRampMap, sampler_BodySkinRampMap, TRANSFORM_TEX(half2(lightBand, 0.5), _BodySkinRampMap));
                    half3 toonDiffuseTint = lerp(_ShadowColor.rgb, half3(1, 1, 1), lightBand);
                    rampTint = lerp(half3(1, 1, 1), rampMap.rgb, _RampTintStrength);
                    rampTint *= toonDiffuseTint *  shadowAO;
                }
                else
                {
                    // 方式 3：不采样 Ramp，二分阴影之外额外叠加交界暖色。               
                    float lightBand = smoothstep(_ShadowThreshold - softness, _ShadowThreshold + softness, NdotL);
                    half3 toonDiffuseTint = lerp(_ShadowColor.rgb, half3(1, 1, 1), lightBand);
                    // 区间外为 0；中心 lightBand=0.5，乘 4 将遮罩峰值归一化为 1。
                    //float shadowEdgeMask = 4.0 * lightBand * (1.0 - lightBand);    
                    float edgeCenter = 0.35;
                    float shadowEdgeMask = smoothstep(0.0, edgeCenter, lightBand) *(1.0 - smoothstep(edgeCenter, 1.0, lightBand));       
                    half3 toonTint = lerp(toonDiffuseTint,toonDiffuseTint * _ShadowEdgeColor.rgb , saturate(shadowEdgeMask * _ShadowEdgeStrength));                             
                    rampTint = toonTint * shadowAO ;
                }

                half3 toonDiffuse = baseMap.rgb  * rampTint;
                toonDiffuse *= mainLight.color.rgb * mainLight.distanceAttenuation ;

                return half4(toonDiffuse.rgb, 1); // 三种方式共用基础色与主光输出，便于比较。
            }
            ENDHLSL
        }

        // 当前主 Pass 是不透明材质，因此深度 Pass 不生成透明裁剪变体。
        // 直接复用共用常量，不能再引入会重复声明 UnityPerMaterial 的 LitInput。
        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // 向相机法线纹理输出几何法线，供 SSAO 等功能使用。
        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            // -------------------------------------
            // Universal Pipeline keywords
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ToonOutline"
            Tags { "LightMode" = "ToonOutline" }

            ZWrite Off
            ZTest LEqual
            Cull Front

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "PunishingOutline.hlsl"
            ENDHLSL
        }
    }
    CustomEditor "PunishingClothShaderGUI"
}
