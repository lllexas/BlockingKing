# LevelGeometric Shader 维护记录

日期：2026-05-09

## 目标

`BlockingKing/LevelGeometric` 是关卡地形 Mesh 使用的主 shader，同时也会被 `Wall.Unstable` 等运行时实体墙复用。

它当前承担三件事：

```text
关卡地形显示
接收其他物体阴影
墙体几何投射阴影
```

## 阴影接收

主 Pass 为：

```hlsl
Name "UniversalForward"
Tags { "LightMode" = "UniversalForward" }
```

接收主光阴影时，不要在 vertex 阶段计算并插值 `shadowCoord`。

错误倾向：

```hlsl
OUT.shadowCoord = TransformWorldToShadowCoord(OUT.positionWS);
```

原因：URP 主光阴影通常使用 cascaded shadow map。cascade split 与摄像机距离相关，如果一个大三角面跨过 split 边界，顶点阶段算出的 shadow coord 会被插值，可能在墙顶或大地面上出现随摄像机移动的斜向黑带。

当前策略是在 fragment 阶段按像素计算：

```hlsl
float4 shadowCoord = TransformWorldToShadowCoord(ws);
Light mainLight = GetMainLight(shadowCoord);
float shadow = mainLight.shadowAttenuation;
```

然后只让直接光漫反射吃阴影：

```hlsl
float3 diffuse = albedo * lightCol * NdotL * shadow;
```

环境光、自发光和 Fresnel 不乘 shadow。

## ShadowCaster

`LevelGeometric` 有独立 `ShadowCaster` Pass，用于让墙体几何进入 shadow map。

当前 ShadowCaster 是极简版本：

```hlsl
output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
```

它没有使用 URP 标准 `ApplyShadowBias`。这样做是为了避免直接 include `Shadows.hlsl` 时触发 URP 14 include 顺序问题。

副作用：如果后续出现 shadow acne 或 peter-panning，再考虑补 bias，但需要谨慎处理 include 顺序。

## 顶点色 Alpha 约定

`LevelMeshBuilder` 用顶点色 alpha 区分几何类型：

```text
alpha = 0 -> 地板
alpha = 1 -> 墙体
alpha = 2 -> Tag marker
```

ShadowCaster 只投 `alpha = 1` 的几何：

```hlsl
clip(input.alpha - 0.5);
clip(1.5 - input.alpha);
```

这样地板不会自投影到自己身上，Tag marker 也不会投影。这个规则目前用于规避大地形 Mesh 的自阴影黑带。

`Wall.Unstable` 运行时 cube 使用同一 shader。Unity primitive mesh 默认顶点色一般为白色，alpha 约等于 1，因此会进入 ShadowCaster。

## GPU Instancing

`DrawSystem` 会通过 `Graphics.DrawMeshInstanced` 绘制部分实体，包括 `Wall.Unstable`。

因此 `LevelGeometric` 的 Forward Pass 和 ShadowCaster Pass 都需要保留：

```hlsl
#pragma multi_compile_instancing
UNITY_VERTEX_INPUT_INSTANCE_ID
UNITY_SETUP_INSTANCE_ID(...)
UNITY_TRANSFER_INSTANCE_ID(...)
```

如果这些宏被移除，材质即使 `enableInstancing = true`，shader variant 也不完整。

## Include 顺序

URP 14 中，`Shadows.hlsl` 会使用 `LerpWhiteTo`，该函数来自 SRP Core 的 `CommonMaterial.hlsl`。

`CommonMaterial.hlsl` 又依赖 `real` 等类型定义，不能放在 `Core.hlsl` 前面。

当前 Forward Pass include 顺序为：

```hlsl
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
```

不要直接裸 include `Shadows.hlsl`，除非确认依赖链已经完整。

## 材质复用

`Wall.Unstable` 的绘制材质由 `LevelPlayer` 当前使用的 `_materialInstance` 传给 `DrawSystem`。

不要在 `DrawSystem` 里新建一份 `BlockingKing/LevelGeometric` 材质，否则会丢失 `LevelPlayer` 上调过的 shader 参数。

当前链路：

```text
LevelPlayer.EnsureLevelMaterial()
  -> _materialInstance
  -> DrawSystem.SetWallMaterial(_materialInstance)
  -> Graphics.DrawMeshInstanced(wallMesh, wallMaterial, ...)
```

