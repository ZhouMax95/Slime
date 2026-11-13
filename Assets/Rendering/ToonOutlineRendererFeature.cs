using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// 在 URP Renderer 的 Add Renderer Feature 菜单中添加此功能；适用于 Unity 6 Render Graph。
public sealed class ToonOutlineRendererFeature : ScriptableRendererFeature
{
    // Shader 中的描边 Pass 必须写 Tags { "LightMode" = "ToonOutline" }。
    public const string OutlineLightMode = "ToonOutline";

    [Tooltip("只绘制这些 Layer 中、不透明材质里带 ToonOutline Pass 的物体。")]
    public LayerMask layerMask = ~0;

    // Feature 负责安排绘制时机，Pass 负责描述具体如何绘制。
    private OutlinePass outlinePass;

    public override void Create()
    {
        // 只创建一次 Pass，后续相机渲染时重复使用。
        outlinePass = new OutlinePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Game 和 Scene 窗口都显示描边，材质预览和反射探针相机跳过。
        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType != CameraType.Game && cameraType != CameraType.SceneView)
            return;

        // 同步 Inspector 的 Layer 设置，再把 Pass 加入当前相机的渲染队列。
        outlinePass.layerMask = layerMask;
        renderer.EnqueuePass(outlinePass);
    }

    private sealed class OutlinePass : ScriptableRenderPass
    {
        public LayerMask layerMask;

        // 用 LightMode 查找 Pass，不是匹配 Shader 名称或 Pass 的 Name。
        private readonly ShaderTagId shaderTag = new ShaderTagId(OutlineLightMode);

        // 把记录阶段创建的绘制列表传递给实际执行的回调。
        private sealed class PassData
        {
            public RendererListHandle rendererList;
        }

        public OutlinePass()
        {
            // 主体和天空绘制后、透明物体之前画描边，避免天空覆盖不写深度的描边。
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // 读取 URP 为当前相机准备的剔除结果、相机信息、光源和渲染目标。
            var renderingData = frameData.Get<UniversalRenderingData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var lightData = frameData.Get<UniversalLightData>();
            var resources = frameData.Get<UniversalResourceData>();

            // 指定描边 LightMode，使用物体原有材质，不替换材质或 Shader。
            var drawing = RenderingUtils.CreateDrawingSettings(
                shaderTag, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);

            // 只选择不透明队列和指定 Layer，排除透明材质。
            var filtering = new FilteringSettings(RenderQueueRange.opaque, layerMask);
            var listParameters = new RendererListParams(renderingData.cullResults, drawing, filtering);

            // 记录一个光栅化 Pass，命名会显示在 Frame Debugger 中。
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Toon Outline", out var passData))
            {
                // 创建绘制列表，并声明此 Pass 使用它，让 Render Graph 正确管理依赖。
                passData.rendererList = renderGraph.CreateRendererList(listParameters);
                builder.UseRendererList(passData.rendererList);

                // 保留已经绘制的颜色，并使用场景深度进行遮挡判断；这里不清屏。
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);

                // 实际执行时绘制列表；顶点外扩、剔除方式、颜色和深度状态由 Shader 决定。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    context.cmd.DrawRendererList(data.rendererList));
            }
        }
    }
}
