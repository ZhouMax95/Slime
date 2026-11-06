Shader "CartoonShading/PunishingCloth"
{
    Properties
    {
        // ==================== 纹理输入 ====================
        // 四张贴图分别保留独立的缩放与偏移，通道用途在片元函数中处理。
        [Header(Texture Inputs)]
        [MainTexture] _BaseMap("基础色贴图 (Base Color)", 2D) = "white" {}
        _AOMap("遮罩贴图 (AO Map)", 2D) = "white" {}
        [Normal] _NormalMap("法线贴图 (Normal Map)", 2D) = "bump" {}
        // R：金属度；G：光滑度；B：AO；A：NPR/PBR 混合权重。
        _PBRMap("材质贴图 (PBR Map)", 2D) = "white" {}

        // ==================== 法线控制 ====================
        // 控制切线空间法线的凹凸强度；设为 0 时使用模型自身法线。
        [Header(Normal Mapping)]
        _NormalScale("法线强度", Range(0, 2)) = 1

        // ==================== NPR 二分阴影 ====================
        [Header(NPR Shadow)]
        // 暗部以基础色乘上此颜色，保留原贴图的颜色细节。
        _ShadowColor("暗部颜色", Color) = (0.5, 0.5, 0.65, 1)
        // 对应原始 NdotL 的明暗分界，值越大，暗部覆盖范围越大。
        _ShadowThreshold("明暗分界阈值", Range(-1, 1)) = 0
        // 阈值两侧的过渡半宽，越接近 0，二分边界越硬。
        _ShadowSoftness("明暗过渡半宽", Range(0, 1)) = 0.05

        // ==================== G 通道固有阴影 ====================
        [Header(Fixed Shadow AO)]
        // 本模型衣服 AOMap.g 的普通区域约为 88/255，不能直接当作白色 1 使用。
        // 先将黑点到白点之间的范围平滑映射为 0~1，避免非遮蔽区域整体变暗。
        _ShadowAOBlackPoint("固有阴影黑点", Range(0, 1)) = 0.02
        _ShadowAOWhitePoint("固有阴影白点", Range(0, 1)) = 0.33
        // 0 完全忽略固有阴影，1 完全使用重映射结果，仅影响 NPR 分支。
        _ShadowAOStrength("固有阴影强度", Range(0, 1)) = 1

        // ==================== NPR 风格化高光 ====================
        [Header(NPR Specular)]
        // 先计算 Blinn-Phong 亮斑，再通过平滑阈值整理成有软边的高光块。
        [HDR] _NPRSpecColor("NPR 高光颜色", Color) = (1, 1, 1, 1)
        _NPRSpecIntensity("NPR 高光强度", Range(0, 5)) = 0.5
        // 指数越大，原始高光范围越小；阈值越高，最终保留的亮斑越小。
        _NPRSpecExponent("NPR 高光指数", Range(1, 256)) = 32
        _NPRSpecThreshold("NPR 高光阈值", Range(0, 1)) = 0.5
        // 控制高光边缘的过渡半宽，接近 0 时呈现硬边。
        _NPRSpecSoftness("NPR 高光过渡半宽", Range(0, 0.5)) = 0.05

        // ==================== PBR 光照与混合 ====================
        [Header(PBR Lighting)]
        // 倍率保留贴图中的相对变化，偏移用于整体提高或降低金属度。
        // 最终金属度 = saturate(贴图 R * 倍率 + 偏移)，默认值保持贴图原值。
        _MetallicScale("金属度倍率", Range(0, 2)) = 1
        _MetallicOffset("金属度偏移", Range(-1, 1)) = 0
        // 先将 G 通道光滑度转换成感知粗糙度，再调节倍率与偏移。
        // 最终粗糙度 = saturate((1 - 贴图 G) * 倍率 + 偏移)。
        // 倍率设为 0 时，可用偏移指定整张材质的固定值。
        _RoughnessScale("粗糙度倍率", Range(0, 2)) = 1
        _RoughnessOffset("粗糙度偏移", Range(-1, 1)) = 0

        // ==================== PBR 直接漫反射模式 ====================
        [Header(PBR Direct Diffuse)]
        // 关闭时保留标准 Lambert；开启时只将直接漫反射的余弦项替换成二分遮罩。
        [ToggleUI] _PBRDiffuseToon("PBR 直接漫反射二分", Float) = 0
        // 独立于 NPR 阈值，便于分别调试两条分支；使用带符号的 NdotL。
        _PBRDiffuseThreshold("PBR 漫反射明暗阈值", Range(-1, 1)) = 0
        _PBRDiffuseSoftness("PBR 漫反射过渡半宽", Range(0, 1)) = 0.05


        // ==================== GGX D 项风格化对比 ====================
        [Header(Stylized GGX Distribution)]
        // 默认关闭以保留 URP 原版高光；开启后只替换 PBR 直接高光内部的 D 项。
        [Toggle(_PBR_SPECULAR_STYLIZED)] _PBRSpecularStylized("启用 GGX D 项风格化", Float) = 0
        // 第一层通常设较低阈值，形成较宽的外层亮斑；D 可以超过 1，阈值不限制在 0~1。
        _GGXFirstThreshold("第一层 D 阈值", Float) = 0.5
        _GGXFirstSoftness("第一层过渡半宽", Range(0, 2)) = 0.1
        _GGXFirstIntensity("第一层亮斑强度", Range(0, 10)) = 1
        // 0 使用原始 GGX D，1 使用阈值亮斑；强度参数仅作用于风格化部分。
        _GGXFirstStyle("第一层风格化权重", Range(0, 1)) = 1
        // 第二层通常设较高阈值，形成较小、较亮的中心亮斑。
        _GGXSecondThreshold("第二层 D 阈值", Float) = 2
        _GGXSecondSoftness("第二层过渡半宽", Range(0, 2)) = 0.1
        _GGXSecondIntensity("第二层亮斑强度", Range(0, 10)) = 2
        _GGXSecondStyle("第二层风格化权重", Range(0, 1)) = 1
        // 0 只使用第一层，1 只使用第二层，0.5 对应参考图中的等权混合。
        _GGXLayerBlend("两层亮斑混合", Range(0, 1)) = 0.5

        // ==================== 独立贴图通道调试 ====================
        [Header(Texture Debug)]
        // 仅勾选时启用调试变体；关闭时不采样调试贴图，也不影响正常光照。
        [Toggle(_DEBUG_TEXTURE)] _DebugTextureEnabled("启用贴图通道调试", Float) = 0
        _DebugMap("调试贴图", 2D) = "white" {}
        // RGB 显示采样颜色；单通道复制到 RGB 显示灰度，输出始终不透明。
        [Enum(RGB, 0, R, 1, G, 2, B, 3, A, 4)] _PreviewChannel("调试显示通道", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            // 将调试采样限制在开启关键字的片元变体中。
            #pragma shader_feature_local_fragment _DEBUG_TEXTURE
            #pragma shader_feature_local_fragment _PBR_SPECULAR_STYLIZED

            // ==================== 管线依赖 ====================
            // Core 提供空间变换与法线解包，Lighting 提供 URP BRDF、主光和环境光计算。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // 独立保存可替换的风格化高光方法，便于与 URP 原版对照。
            #include "PunishingClothSpecular.hlsl"

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
            TEXTURE2D(_PBRMap);
            SAMPLER(sampler_PBRMap);
            TEXTURE2D(_DebugMap);
            SAMPLER(sampler_DebugMap);

            // ==================== 材质常量 ====================
            // 材质参数集中在同一个常量缓冲区，以兼容 SRP Batcher。
            CBUFFER_START(UnityPerMaterial)
                // 各贴图的缩放与偏移。
                float4 _BaseMap_ST;
                float4 _AOMap_ST;
                float4 _NormalMap_ST;
                float4 _PBRMap_ST;
                float4 _DebugMap_ST;
                float _NormalScale;
                // NPR 明暗分层参数。
                half4 _ShadowColor;
                float _ShadowThreshold;
                float _ShadowSoftness;
                // 固有阴影的通道范围校正与强度控制。
                float _ShadowAOBlackPoint;
                float _ShadowAOWhitePoint;
                float _ShadowAOStrength;
                // NPR 高光的颜色、强度、形状与边缘控制。
                half4 _NPRSpecColor;
                float _NPRSpecIntensity;
                float _NPRSpecExponent;
                float _NPRSpecThreshold;
                float _NPRSpecSoftness;
                // PBR 贴图通道的数值调节，默认不改变贴图原有表现。
                float _MetallicScale;
                float _MetallicOffset;
                float _RoughnessScale;
                float _RoughnessOffset;
                // PBR 直接漫反射的标准/二分切换与独立过渡参数。
                float _PBRDiffuseToon;
                float _PBRDiffuseThreshold;
                float _PBRDiffuseSoftness;
               
                // GGX D 项的两层阈值与强度参数。
                float _PBRSpecularStylized;
                float _GGXFirstThreshold;
                float _GGXFirstSoftness;
                float _GGXFirstIntensity;
                float _GGXFirstStyle;
                float _GGXSecondThreshold;
                float _GGXSecondSoftness;
                float _GGXSecondIntensity;
                float _GGXSecondStyle;
                float _GGXLayerBlend;

                // 调试属性与关键字开关对应，保留在材质常量中供 Inspector 编辑。
                float _DebugTextureEnabled;
                float _PreviewChannel;
            CBUFFER_END

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
                // ---------- 调试输出：启用时直接查看独立贴图 ----------
                // 提前返回，避免主光、AO、高光和 NPR/PBR 混合改变通道观察结果。
                #if defined(_DEBUG_TEXTURE)
                    half4 debugMap = SAMPLE_TEXTURE2D(_DebugMap, sampler_DebugMap, TRANSFORM_TEX(IN.uv, _DebugMap));
                    if (_PreviewChannel == 1) return half4(debugMap.rrr, 1);
                    if (_PreviewChannel == 2) return half4(debugMap.ggg, 1);
                    if (_PreviewChannel == 3) return half4(debugMap.bbb, 1);
                    if (_PreviewChannel == 4) return half4(debugMap.aaa, 1);
                    return half4(debugMap.rgb, 1);
                #endif

                // ---------- 1. 纹理采样 ----------
                // 保留完整 RGBA，方便后续逐通道定义遮罩与材质用途。
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(IN.uv, _BaseMap));
                half4 aoMap = SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, TRANSFORM_TEX(IN.uv, _AOMap));
                half4 normalMap = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, TRANSFORM_TEX(IN.uv, _NormalMap));
                half4 pbrMap = SAMPLE_TEXTURE2D(_PBRMap, sampler_PBRMap, TRANSFORM_TEX(IN.uv, _PBRMap));
                // PBRMap 提供材质参数；AOMap 的 G 通道提供 NPR 固有阴影。
                half metallic = saturate(pbrMap.r * _MetallicScale + _MetallicOffset);
                // 调节感知粗糙度后转换回 URP BRDF 接口所需的光滑度。
                // 粗糙度越高，高光越宽、环境反射越模糊；金属度与粗糙度相互独立。
                half perceptualRoughness = saturate((1.0h - pbrMap.g) * _RoughnessScale + _RoughnessOffset);
                half smoothness = 1.0h - perceptualRoughness;
                half ao = saturate(pbrMap.b);
                half NPRPNRBlend = saturate(pbrMap.a);

                // ---------- 2. 逐像素法线 ----------
                // 使用插值后的 TBN 转换贴图法线；最终归一化需保留在片元阶段。
                half3 normalTS = UnpackNormalScale(normalMap, _NormalScale);
                float3x3 tangentToWorld = float3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS);
                float3 normalWS = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                // ---------- 3. 光照明暗分界 ----------
                half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                Light mainLight = GetMainLight();
                // 无参 GetMainLight 不采样阴影贴图，shadowAttenuation 当前恒为 1。
                // distanceAttenuation 是距离/范围衰减；主方向光中还用于表示是否被剔除。

                // 保留负值，使阈值可以调节到背光侧，扩大亮部范围。
                float NdotL = dot(normalWS, mainLight.direction);
                // 以阈值为中心生成柔和的二分边界，并避免过渡宽度为零。
                float softness = max(_ShadowSoftness, 0.0001);
                float lightBand = smoothstep(_ShadowThreshold - softness, _ShadowThreshold + softness, NdotL);

                // ---------- 3.1. 固有阴影范围重映射 ----------
                // 普通灰底达到白点后输出 1，不再限制正常亮部；黑色凹陷输出 0。
                // 黑白点意外颠倒或相等时，仍保持有效的递增过渡区间。
                float shadowAOStart = min(_ShadowAOBlackPoint, _ShadowAOWhitePoint);
                float shadowAOEnd = max(max(_ShadowAOBlackPoint, _ShadowAOWhitePoint), shadowAOStart + 0.0001);
                float remappedShadowAO = smoothstep(shadowAOStart, shadowAOEnd, aoMap.g);
                float shadowAO = lerp(1.0, remappedShadowAO, _ShadowAOStrength);
                shadowAO = saturate(shadowAO);
                float finalLightBand = lightBand * shadowAO;

                // ---------- 4. NPR 漫反射合成 ----------
                // 固有阴影只使局部进入已定义的暗部颜色，不直接把基础色乘黑。
                // 保留现有 PBRMap.b 对 NPR 的 AO 调节，方便与原有表现对照。
                half3 shadowColor = baseMap.rgb * _ShadowColor.rgb;
                half3 NPRDiffuse = lerp(shadowColor, baseMap.rgb, finalLightBand);
                NPRDiffuse *= mainLight.color * mainLight.distanceAttenuation * ao;

                // ---------- 5. NPR 风格化高光 ----------
                // 观察方向已在光照分界前统一计算；保留原有注释代码供后续对照。
                // float3 halfDirectionWS = SafeNormalize(float3(mainLight.direction) + float3(viewDirectionWS));
                // float NdotH = saturate(dot(normalWS, halfDirectionWS));
                // float nprSpecular = pow(NdotH, max(_NPRSpecExponent, 1.0));
                // float specSoftness = max(_NPRSpecSoftness, 0.0001);
                // float specBand = smoothstep(_NPRSpecThreshold - specSoftness,
                //     _NPRSpecThreshold + specSoftness, nprSpecular);
                // // 亮面遮罩控制风格化暗部，余弦项抑制背光侧高光。
                // half3 NPRSpecular = _NPRSpecColor.rgb * _NPRSpecIntensity * specBand
                //     * mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation
                //     * finalLightBand * saturate(NdotL);

                // ---------- 6. PBR 材质参数 ----------
                // URP 内部将光滑度转换成粗糙度，并计算金属工作流的能量分配与反射颜色。
                half alpha = 1;
                BRDFData brdfData;
                InitializeBRDFData(baseMap.rgb, metallic, half3(0, 0, 0), smoothness, alpha, brdfData);

                // ---------- 7. PBR 直接光：漫反射与高光 ----------
                // 主光颜色与衰减独立于余弦项，避免漫反射二分时误改高光的入射光。
                half3 directLightColor = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half3 directRadiance = directLightColor * saturate(NdotL);
                half3 PBRDirectDiffuse = brdfData.diffuse * directRadiance;
                if (_PBRDiffuseToon > 0.5)
                {
                    // smoothstep 直接替换 Lambert 项，不再重复乘 NdotL，以保留明确的明暗色阶。
                    // 暗部的直接漫反射为 0，PBR 环境光仍按原逻辑提供间接照明。
                    float diffuseSoftness = max(_PBRDiffuseSoftness, 0.0001);
                    float diffuseBand = smoothstep(_PBRDiffuseThreshold - diffuseSoftness,
                        _PBRDiffuseThreshold + diffuseSoftness, NdotL);
                    PBRDirectDiffuse = brdfData.diffuse * directLightColor * diffuseBand;
                }
                // 只切换直接高光的标量 BRDF，保留同一套反射颜色与光照输入进行对比。
                #if defined(_PBR_SPECULAR_STYLIZED)
                    // 沿用当前 AOMap.r 作为 D 项风格化的形状遮罩。
                    half specularTerm = PunishingCloth_DirectBRDFSpecularStylized(
                        brdfData, normalWS, mainLight.direction, viewDirectionWS,
                        float4(_GGXFirstThreshold, _GGXFirstSoftness, _GGXFirstIntensity, _GGXFirstStyle),
                        float4(_GGXSecondThreshold, _GGXSecondSoftness, _GGXSecondIntensity, _GGXSecondStyle),
                        _GGXLayerBlend, aoMap.r);
                #else
                    half specularTerm = DirectBRDFSpecular(brdfData, normalWS, mainLight.direction, viewDirectionWS);
                #endif
                half3 PBRDirectSpecular = brdfData.specular * specularTerm * directRadiance;

                // ---------- 8. PBR 间接光：环境漫反射与反射探针 ----------
                // 球谐提供基础环境照明，反射探针/天空环境提供金属所需的环境反射。
                // PBR 的 AO 只衰减间接光，不将其重复乘到直接光上。
                half3 ambientSH = SampleSHPixel(half3(0, 0, 0), normalWS);
                half3 reflectionWS = reflect(-viewDirectionWS, normalWS);
                half3 environmentReflection = GlossyEnvironmentReflection(reflectionWS,
                    IN.positionWS, brdfData.perceptualRoughness, 1.0h);
                half fresnelTerm = Pow4(1.0h - saturate(dot(normalWS, viewDirectionWS)));
                half3 PBRIndirectDiffuse = ambientSH * brdfData.diffuse * ao;
                half3 PBRIndirectSpecular = environmentReflection
                    * EnvironmentBRDFSpecular(brdfData, fresnelTerm) * ao;

                // ---------- 9. NPR / PBR 最终混合 ----------
                // 各自合成漫反射与高光，再按 PBRMap.a 插值，避免重复叠加两套高光。
                half3 NPRColor = NPRDiffuse ;//+ NPRSpecular;
                half3 PBRColor = PBRDirectDiffuse + PBRDirectSpecular + PBRIndirectDiffuse  + PBRIndirectSpecular * 0.1;
                half3 finalColor = lerp(NPRColor, PBRColor, NPRPNRBlend);
                return half4(finalColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
