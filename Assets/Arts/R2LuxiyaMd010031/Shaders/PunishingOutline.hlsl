#ifndef PUNISHING_OUTLINE_INCLUDED
#define PUNISHING_OUTLINE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"

 // 材质常量已由共用 HLSLINCLUDE 提供，这里不要重复声明 CBUFFER。

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 vertexColor : COLOR;
        float4 tangentOS : TANGENT;
    };

    struct Varyings
    {
        float4 positionHCS : SV_POSITION;
    };

    // ---------- 观察空间：位置与法线都必须先转换到 View 空间 ----------
    float4 vertVSOutline(float3 positionVS, float3 outlineNormalVS)
    {

        float refrenceDepth = 1.0;
        float depth = max(-positionVS.z, 0.001);
        float depthScale = min(depth / refrenceDepth, 1.0);
        float width = _OutlineWidth * depthScale;

        // 保留你的完整 XYZ 外扩，未压平法线 z；通常与世界空间外扩等价。
        positionVS.xy += outlineNormalVS.xy * width * 0.01;
        // View -> Clip 只需要投影矩阵，不能再次乘世界到观察矩阵。
        return mul(GetViewToHClipMatrix(), float4(positionVS, 1.0));
    }

    // ---------- 世界空间：宽度随透视距离缩小 ----------
    float4 vertWSOutline(float3 positionWS, float3 outlineNormalWS)
    {
        positionWS += outlineNormalWS * _OutlineWidth * 0.01;
        return TransformWorldToHClip(positionWS);
    }

    // ---------- 原版一：按屏幕比例外扩，保留原来的 * 0.01 ----------
    float4 vertCSOutlineOriginalRatio(float3 positionWS, float3 outlineNormalWS)
    {
        float4 positionCS = TransformWorldToHClip(positionWS);
        float3 outlineNormalCS = TransformWorldToHClipDir(outlineNormalWS);
        float2 directionCS = outlineNormalCS.xy;
        directionCS /= max(length(directionCS), 0.0001);
        positionCS.xy += directionCS * _OutlineWidth * 0.01 * positionCS.w;
        return positionCS;
    }

    // ---------- 原版二：恢复原先注释中的像素公式，仍使用 _OutlineWidth ----------
    float4 vertCSOutlineOriginalPixels(float3 positionWS, float3 outlineNormalWS)
    {
        float4 positionCS = TransformWorldToHClip(positionWS);
        float3 outlineNormalCS = TransformWorldToHClipDir(outlineNormalWS);
        float2 screenSize = _ScaledScreenParams.xy;
        float2 directionPixels = outlineNormalCS.xy * screenSize;
        directionPixels /= max(length(directionPixels), 0.0001);

        float2 offsetNDC = directionPixels * _OutlineWidth * 2.0 / screenSize;
        positionCS.xy += offsetNDC * positionCS.w;
        return positionCS;
    }

    // ---------- 保留版：透视校正方向，使用独立的像素宽度 ----------
    float4 vertCSOutlinePerspective(float3 positionWS, float3 outlineNormalWS)
    {
        // ---------- 1. 计算透视校正后的屏幕外扩方向 ----------
        float4 positionCS = TransformWorldToHClip(positionWS);
        // 方向使用 w=0 参与变换，不带入相机平移；保留投影后的 w 分量。
        float4 directionCS = mul(GetWorldToHClipMatrix(), float4(outlineNormalWS, 0.0));
        float2 screenSize = max(_ScaledScreenParams.xy, float2(1, 1));

        // 透视校正：对 NDC = positionCS.xy / positionCS.w 求方向导数。
        // 分母 w² 是共同的正数，后续归一化会抵消，因此只保留分子。
        float2 directionNDC = directionCS.xy * positionCS.w - positionCS.xy * directionCS.w;
        // 在像素尺度下归一化，避免横向、纵向因宽高比产生不同宽度。
        float2 directionPixels = directionNDC * screenSize;
        directionPixels /= max(length(directionPixels), 0.0001);

        // ---------- 2. 像素宽度换算为 NDC 偏移 ----------
        // NDC 跨度是 2，所以一个像素对应 2 / 分辨率。
        float2 offsetNDC = directionPixels * _OutlineWidthPixels * 2.0 / screenSize;

        // ---------- 3. 选择线宽随距离变化的方式 ----------
        if (_OutlineDepthScaling > 0.5)
        {
            if (_OutlineWidthScalingMode < 0.5)
            {
                // 相机深度：保留你的限幅算法，参考深度仍为 1。
                float referenceDepth = 1.0;
                float depth = max(-TransformWorldToView(positionWS).z, 0.001);
                float depthScale = min(depth / referenceDepth, 1.0);

                // 不乘 w：近处抵消透视放大，超过参考深度后线宽随距离缩小。
                positionCS.xy += offsetNDC * depthScale;
            }
            else if (_OutlineWidthScalingMode < 1.5)
            {
                // Bounds 屏幕占比：原 C# 把计算好的线宽写入 _OutlineWidth。
                // 这里不再乘深度倍率，避免同一份距离影响被重复计算。
                float boundsWidthPixels = max(_OutlineWidth, 0.0);
                float2 boundsOffsetNDC = directionPixels * boundsWidthPixels * 2.0 / screenSize;

                // 乘 w：GPU 除 w 后，像素宽度恰好等于 C# 传入的值。
                positionCS.xy += boundsOffsetNDC * positionCS.w;
            }
            else
            {
                float3 characterCenter = TransformObjectToWorld(half3(0,0,0)).xyz;
                float3 centerVS = TransformWorldToView(characterCenter);
                float depth = max(-centerVS.z, 0.001);
                float p11 = abs(GetViewToHClipMatrix()[1][1]);
                // 高度由材质控制；负数按 0 处理，避免外扩方向翻转。
                float heightRatio = max(_OutlineCharacterHeight, 0.0) * p11 / (2*depth);
                positionCS.xy += offsetNDC * heightRatio * positionCS.w;
                
            }
        }
        else
        {
            // 不勾选：乘 w 抵消后续透视除法，保持原来的固定像素宽度。
            positionCS.xy += offsetNDC * positionCS.w;
        }
        // 保留原先的 z 减法及偏移单位，改由材质控制。
        positionCS.z -= _OutlineZOffset;
        return positionCS;
    }

    // ---------- 屏幕模式选择：三种实现分开保留，方便逐行对比 ----------
    float4 vertCSOutline(float3 positionWS, float3 outlineNormalWS)
    {
        if (_OutlineProjectionMode < 0.5)
            return vertCSOutlineOriginalPixels(positionWS, outlineNormalWS);
        else if (_OutlineProjectionMode < 1.5)
            return vertCSOutlinePerspective(positionWS, outlineNormalWS);
        else
            return vertCSOutlineOriginalRatio(positionWS, outlineNormalWS);
    }

    // ---------- 共用顶点入口：解码一次，再按材质参数选择实现 ----------
    Varyings vertOutline(Attributes IN)
    {
        Varyings OUT = (Varyings)0;
        VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
        // 材质选择必须与 Mesh 烘焙时的编码一致，三种解码都还原为切线空间法线。
        float3 smoothNormalTS;
        if (_OutlineNormalEncoding < 0.5)
        {
            smoothNormalTS = normalize(IN.vertexColor.rgb * 2.0 - 1.0);
        }
        else if (_OutlineNormalEncoding < 1.5)
        {
            smoothNormalTS = UnpackNormalOctQuadEncode(IN.vertexColor.rg * 2.0 - 1.0);
        }
        else
        {
            smoothNormalTS = UnpackNormalHemiOctEncode(IN.vertexColor.rg * 2.0 - 1.0);
        }

        VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
        float3x3 tangentToWorld = float3x3(
            normalInputs.tangentWS,
            normalInputs.bitangentWS,
            normalInputs.normalWS
        );
        float3 outlineNormalWS = normalize(TransformTangentToWorld(smoothNormalTS, tangentToWorld));

        // 材质统一参数控制分支，无需手动改 #pragma，也不增加描边关键字变体。
        if (_OutlineMode < 0.5)
        {
            OUT.positionHCS = vertWSOutline(positionInputs.positionWS, outlineNormalWS);
        }
        else if (_OutlineMode < 1.5)
        {
            OUT.positionHCS = vertCSOutline(positionInputs.positionWS, outlineNormalWS);
        }
        else
        {
            // 法线按方向转换，不带平移；与 positionVS 保持相同坐标空间。
            float3 outlineNormalVS = TransformWorldToViewNormal(outlineNormalWS, true);
            OUT.positionHCS = vertVSOutline(positionInputs.positionVS, outlineNormalVS);
        }

        return OUT;
    }

    half4 frag(Varyings IN) : SV_Target
    {
        return half4(_OutlineColor.rgb,1);
    }

#endif
