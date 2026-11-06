#ifndef PUNISHING_CLOTH_SPECULAR_INCLUDED
#define PUNISHING_CLOTH_SPECULAR_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"

// ==================== 单层 D 项风格化 ====================
// 参数依次为阈值、过渡半宽、亮斑强度；较低阈值可形成宽亮斑。
// D 是分布函数，可以大于 1，因此在阈值处理前不做 saturate。
float PunishingCloth_ToonDistributionBand(float distribution, float3 layer)
{
    float threshold = max(layer.x, 0.0);
    float softness = max(layer.y, 0.0001);
    return smoothstep(threshold - softness, threshold + softness, distribution) * max(layer.z, 0.0);
}

// ==================== 风格化直接光高光 BRDF ====================
// 基于当前项目 URP 17.2 BRDF.hlsl 的 DirectBRDFSpecular 拆分。
// 只替换内部 GGX D 项，保留 URP 合并的可见性/菲涅尔近似；这是风格化改写。
// 返回值与原方法一样是标量：调用方还需乘 brdfData.specular、光色、衰减与 NdotL。
// firstLayer / secondLayer：各自的（阈值、过渡半宽、强度、风格化权重）。
// 风格化权重为 0 时使用原始 D；为 1 时使用该层的平滑阈值亮斑。
// distributionMask：对应参考图中 D * SpecularExpMask；1 表示不使用形状遮罩。
half PunishingCloth_DirectBRDFSpecularStylized(
    BRDFData brdfData,
    half3 normalWS,
    half3 lightDirectionWS,
    half3 viewDirectionWS,
    float4 firstLayer,
    float4 secondLayer,
    float layerBlend,
    float distributionMask)
{
    // ---------- 0. 标准 PBR 对照 ----------
    // 只要参与混合的层都不使用风格化，直接调用 URP 原方法。
    // 这样标准端点连浮点运算顺序都不改变，避免拆分除法产生精度差异。
    float blend = saturate(layerBlend);
    float firstStyle = saturate(firstLayer.w);
    float secondStyle = saturate(secondLayer.w);
    float effectiveStyle = lerp(firstStyle, secondStyle, blend);
    if (effectiveStyle <= 0.0)
        return DirectBRDFSpecular(brdfData, normalWS, lightDirectionWS, viewDirectionWS);

    // ---------- 1. 光照角度 ----------
    float3 halfDirectionWS = SafeNormalize(float3(lightDirectionWS) + float3(viewDirectionWS));
    float NdotH = saturate(dot(float3(normalWS), halfDirectionWS));
    half LdotH = half(saturate(dot(float3(lightDirectionWS), halfDirectionWS)));

    // ---------- 2. 从 URP 原式中拆出 GGX 分布 ----------
    // URP 的归一化约定已吸收 PI 因子，这里的 D 与标准含 1/PI 的 NDF 尺度不同。
    // 不额外加入 PI，以便与原 DirectBRDFSpecular 在同一尺度下进行比较。
    // brdfData.roughness2 已包含粗糙度变换，不再对贴图粗糙度重复平方。
    float d = NdotH * NdotH * brdfData.roughness2MinusOne + 1.00001f;
    float distributionGGX = brdfData.roughness2 / (d * d);

    // ---------- 3. 两层亮斑 ----------
    // 阈值前乘遮罩可控制亮斑形状；先不假定 AOMap 或其他贴图的具体通道。
    float maskedDistribution = distributionGGX * saturate(distributionMask);
    float firstBand = PunishingCloth_ToonDistributionBand(maskedDistribution, firstLayer.xyz);
    float secondBand = PunishingCloth_ToonDistributionBand(maskedDistribution, secondLayer.xyz);
    // 每层独立保留原始 D，可分别设置成标准高光、风格化高光或两者的过渡。
    // 亮斑强度只作用于风格化结果，权重为 0 时不改变标准高光的亮度。
    float firstDistribution = lerp(distributionGGX, firstBand, firstStyle);
    float secondDistribution = lerp(distributionGGX, secondBand, secondStyle);
    float toonDistribution = lerp(firstDistribution, secondDistribution, blend);

    // ---------- 4. 保留 URP 的 V/F 合并近似 ----------
    // 分母、移动平台限幅与原方法保持一致，只将原 D 换成风格化 D。
    // 不重复乘 BaseColor：调用方的 brdfData.specular 已包含金属工作流的反射颜色。
    half LdotH2 = LdotH * LdotH;
    half specularTerm = toonDistribution / (max(0.1h, LdotH2) * brdfData.normalizationTerm);

    #if REAL_IS_HALF
        specularTerm = specularTerm - HALF_MIN;
        specularTerm = clamp(specularTerm, 0.0, 1000.0);
    #endif

    return specularTerm;
}

#endif
