# Unity Slime Learning Project

这是一个以理解实现过程为优先的 Unity 软体史莱姆学习项目。项目从 CPU 质点弹簧系统开始，逐步加入碰撞、形状保持、Metaball 密度场和 Marching Cubes 动态表面。

第一版不以性能优化为目标，重点是让每个阶段的数据和算法都可以通过代码与 Gizmos 直接观察。

## 环境

- Unity `6000.2.10f1`
- Universal Render Pipeline `17.2.0`
- Input System `1.14.2`
- 主要逻辑使用 C# 在 CPU 上执行

首次打开项目时，Unity 会根据 `Packages/manifest.json` 恢复依赖并重新生成 `Library`、解决方案和项目文件。

## 目录

```text
Assets/
|-- SlimeDemo/                  # 完整参考实现，类型使用 Origin 前缀
|   |-- Scenes/SlimeDemo.unity
|   `-- Scripts/
`-- SlimeDemoByMySelf/          # 手动复现与学习代码
    `-- Scripts/Physics/
        |-- SlimeParticle.cs
        |-- SlimeSpring.cs
        `-- SlimeSoftBody.cs
```

`Assets/SlimeDemo` 用于对照最终结构；实际学习代码保留 `SlimeParticle`、`SlimeSpring` 和 `SlimeSoftBody` 等无前缀命名。

## 当前实现

手动复现版本目前包含：

- 根据初始 `SphereCollider` 在球体内部生成规则体积质点。
- 使用结构、面对角线和体对角线规则建立弹簧网络。
- 使用胡克定律和弹簧阻尼计算内部受力。
- 使用带子步的半隐式欧拉法更新速度和位置。
- 将质点作为小球处理无限水平地面碰撞。
- 支持基础反弹、切向速度衰减和接地状态。
- 使用 Gizmos 显示质点、弹簧和碰撞半径。

当前版本尚未处理普通 Cube 等任意 Unity Collider，也还没有生成最终可见的动态史莱姆表面。

## 运行

1. 使用 Unity `6000.2.10f1` 打开项目根目录。
2. 打开 `Assets/SlimeDemo/Scenes/SlimeDemo.unity`。
3. 选中场景中的 `Slime` 对象。
4. 保持 `OriginSlimeSoftBody` 禁用，并启用 `Slime.SlimeSoftBody`。
5. 进入 Play Mode，在 Scene 窗口打开 Gizmos 观察质点和弹簧。

当前手动复现版本没有玩家控制。质点会在重力作用下落到 `groundHeight` 指定的无限水平面。

## 模拟流程

每个物理子步按以下顺序执行：

```text
清空并施加外力
    -> 累加弹簧力与阻尼
    -> 半隐式欧拉积分
    -> 修正地面穿透
    -> 处理反弹和切向摩擦
```

质点是物理状态，弹簧是质点之间的连接约束。后续的密度场与 Marching Cubes Mesh 只负责从质点状态重建平滑外观，不参与主要软体受力。

## 后续计划

1. 加入相对质心的简化形状保持力，减少长期塌缩。
2. 支持 Box、Sphere 等普通 3D Collider。
3. 添加水平移动、落地判断和跳跃。
4. 从质点生成 Metaball 密度场。
5. 使用 CPU Marching Cubes 动态重建 Mesh。
6. 添加史莱姆材质与相机跟随。
7. 完成基础版本后再评估 Jobs、Burst 或 Compute Shader 优化。

## Git 说明

应提交以下内容：

- `Assets/` 及其 `.meta` 文件
- `Packages/manifest.json` 和 `Packages/packages-lock.json`
- `ProjectSettings/`

不要提交 `Library/`、`Temp/`、`Logs/`、IDE 配置以及 Unity 自动生成的 `.csproj`、`.sln` 文件；这些内容已经由根目录 `.gitignore` 排除。

## 参考

- [lamp-cap/Unity_Slime](https://github.com/lamp-cap/Unity_Slime)

参考项目使用了更复杂的数据结构与优化方案。本项目第一阶段有意保留单线程 CPU 质点弹簧实现，便于逐步阅读、验证和调试。
