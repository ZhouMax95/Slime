# Slime Demo V0.1

这是一个以可读性为优先的 CPU 软体史莱姆原型。

打开 `Scenes/SlimeDemo.unity` 后进入 Play Mode 即可运行。

## 操作

- `WASD`：相对相机方向移动
- `Space`：落地时跳跃
- `R`：回到初始位置并重新生成质点、弹簧和表面

## 推荐阅读顺序

1. `OriginSlimeParticle.cs`：单个物理质点的数据。
2. `OriginSlimeSpring.cs`：两个质点之间的弹簧数据。
3. `OriginSlimeSoftBody.cs`：质点生成、弹簧网络、受力、积分、碰撞和体积近似保持。
4. `OriginDensityField.cs`：把质点转换成 Metaball 密度场。
5. `OriginMarchingCubes.cs`：从密度场提取动态 Mesh。
6. `OriginMarchingCubesTables.cs`：标准 256-case 查找表，初学时可以先跳过。
7. `OriginSlimePlayerController.cs` 和 `OriginSlimeCameraFollow.cs`：输入与相机。

## 最有用的 Inspector 参数

- `Spring Stiffness`：越大越硬。
- `Spring Damping`：越大越快停止抖动。
- `Volume Stiffness`：越大越不容易被压扁。
- `Collision Iterations`：每个子步处理普通 3D Collider 的次数，墙角穿透时可以适当提高。
- `Collidable Layers`：只有这个 LayerMask 中的非 Trigger Collider 会参与质点碰撞。
- `Metaball Radius`：越大，质点之间融合得越平滑。
- `Iso Level`：改变可见表面的胖瘦。
- `Density Smoothing Passes`：密度场平滑次数。初版建议保持 `1`，设为 `0` 可观察未平滑的效果。
- `Show Particles / Springs / Density Bounds`：观察三个阶段的数据。

`OriginSlimeSoftBody` 组件右键菜单中的 `Reinitialize Slime` 也可以在运行时重新初始化。

初版的 `Volume Preservation` 实际上是一种简单的形状恢复力：质点会轻微回到相对当前质心的初始偏移。它不是严格的体积约束，但能防止弹簧网格长期运行后折叠成薄片，并且比 PBD / XPBD 更容易直接阅读。

## 当前限制

V0.1 支持无限水平地面以及静态或运动学 3D Collider。碰撞由质点球处理，不使用动态 MeshCollider，也不会反向推动带 Rigidbody 的物体。质点是物理状态，Density Field 是形状，Marching Cubes Mesh 只负责显示。当前没有 Compute Shader、Jobs、Burst、XPBD、自碰撞、分裂或合并。

## 参考

表面重建参考了 [lamp-cap/Unity_Slime](https://github.com/lamp-cap/Unity_Slime) 中的密度网格平滑与密度梯度法线思路。参考项目使用 PBF、Jobs 和 Burst；本项目第一版有意保留 CPU 单线程的 Mass-Spring 结构，方便逐步阅读和调试。
