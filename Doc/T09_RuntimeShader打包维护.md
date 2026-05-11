# T09 Runtime Shader 打包维护

日期：2026-05-11

## 背景

这次 Player Build 出现过三类现象：

```text
PathFlow 正常显示
DrawSystem 的单位/数字不显示
Box 外扩玻璃不显示
```

根因不是相机或 ECS 绘制链路，而是运行时材质依赖的 shader 没有稳定进入 Player 包。

Unity Editor 下 `Shader.Find(...)` 很容易成功，但 Player Build 会做 shader stripping。只在代码里通过 `Shader.Find` 访问的 shader，可能没有任何资产引用链，因此会被剥掉。

## 关键结论

### 不要把 URP/Lit 放进 Always Included

`Universal Render Pipeline/Lit` 是大 shader。把它加入 `Always Included Shaders` 会触发大量 `ForwardLit` 变体编译。

这次验证中，直接全局保留 Lit 会出现约 `20000 fp` 级别的变体编译；改成材质引用后，Lit 编译量降到约 `72 fp`，单位也能正常显示。

结论：

```text
Always Included 适合小型自定义 shader。
URP/Lit 这类大 shader 应优先通过实际 Material 引用进入包。
```

### Runtime new Material 应该 clone fallback material

错误倾向：

```csharp
var shader = Shader.Find("Universal Render Pipeline/Lit");
var material = new Material(shader);
```

问题：

```text
1. Player 中 Shader.Find 可能失败。
2. 即使成功，也没有实际材质状态约束，容易扩大变体或丢变体。
3. 多个 runtime 系统各自裸找 shader，维护成本会失控。
```

当前策略：

```text
Assets/Resources/DrawSystemLitFallback.mat
  -> 引用 Universal Render Pipeline/Lit
  -> DrawSystem clone 这个材质
  -> clone 后只改颜色和 instancing
```

这样 Lit 通过真实材质资产进入引用链，Unity 只收集该材质实际需要的变体。

### 自定义 runtime shader 也要有 fallback material

`BlockingKing/BoxGlass` 是自定义 shader，但如果只在代码中：

```csharp
Shader.Find("BlockingKing/BoxGlass")
```

Player Build 里仍可能被剥掉。

当前策略：

```text
Assets/Resources/DrawSystemBoxGlassFallback.mat
  -> 引用 BlockingKing/BoxGlass
  -> DrawSystem clone 这个材质绘制 box 外扩玻璃
```

这保证 BoxGlass 通过实际材质引用进包。

## 当前工程链路

### Editor 工具

入口：

```text
Tools/BlockingKing/Rendering/Create or Update Shader Registry
```

当前会做三件事：

```text
1. 创建或更新 Assets/Settings/Rendering/BlockingKingShaderRegistry.asset
2. 创建 runtime fallback materials
3. 同步 ProjectSettings/GraphicsSettings.asset 的 Always Included Shaders
```

自动创建的 fallback material：

```text
Assets/Resources/DrawSystemLitFallback.mat
Assets/Resources/DrawSystemBoxGlassFallback.mat
```

### DrawSystem

单位材质：

```text
Inspector 明确配置的 player/box/enemy material
  -> unitFallbackMaterial
  -> Resources/DrawSystemLitFallback.mat
  -> 最后才 Shader.Find("Universal Render Pipeline/Lit")
```

Box 外扩玻璃：

```text
Inspector 明确配置的 boxGlass/coreBoxGlass material
  -> boxGlassFallbackMaterial
  -> Resources/DrawSystemBoxGlassFallback.mat
  -> 最后才 Shader.Find("BlockingKing/BoxGlass")
```

最后的 `Shader.Find` 只作为诊断兜底，不应作为正常打包路径。

## Shader Registry 的边界

`BlockingKingShaderRegistrySO` 有两个字段：

```text
alwaysIncludedShaders
runtimeMaterials
```

建议用法：

| 类型 | 推荐做法 |
|---|---|
| 小型自定义 shader，例如 `BlockingKing/GridOverlay/PathFlow` | 可以进 `alwaysIncludedShaders` |
| 大型 URP 内置 shader，例如 `URP/Lit` | 不进 `alwaysIncludedShaders`，用 material 引用 |
| runtime 创建材质依赖的 shader | 创建 fallback material，并加入引用链 |
| 只在 Editor/调试中使用的 shader | 不要进打包配置 |

同步工具会主动从 `Always Included Shaders` 里移除这些全局 fallback shader：

```text
Universal Render Pipeline/Lit
Universal Render Pipeline/Simple Lit
Universal Render Pipeline/Unlit
Standard
Unlit/Color
Sprites/Default
```

原因是它们都可能带来不必要的全局变体保留。需要用它们时，应通过具体材质引用。

## 后续新增 runtime shader 的流程

新增一个会在运行时使用的 shader 时，按这个顺序处理：

1. 先判断它是小 shader 还是大 shader。
2. 如果是小型自定义 shader，可以加入 shader registry。
3. 如果会通过 runtime `new Material` 使用，优先创建一个 `.mat` fallback。
4. Runtime 代码 clone fallback material，不直接依赖 `Shader.Find`。
5. 点一次：

```text
Tools/BlockingKing/Rendering/Create or Update Shader Registry
```

6. 打 Player Build，观察 shader variant 数量。

## 排查清单

Player Build 视觉丢失时，按这个顺序查：

```text
1. Editor 正常、Player 不正常？
   是 -> 优先怀疑 shader stripping 或 Resources/Addressables 引用链。

2. 是否只靠 Shader.Find？
   是 -> 建 fallback material。

3. 是否把大 shader 放进 Always Included？
   是 -> 移除，改用材质引用。

4. Graphics.DrawMeshInstanced 是否需要 instancing variant？
   是 -> fallback material 和 runtime clone 都要 enableInstancing = true。

5. Player log 是否有 DrawSystem/LevelPlayer 的 shader warning？
   有 -> 按 warning 指向补材质或 registry。
```

## 当前已知稳定方案

```text
DrawSystem unit:
  DrawSystemLitFallback.mat -> URP/Lit -> clone -> SetColor -> DrawMeshInstanced

DrawSystem box glass:
  DrawSystemBoxGlassFallback.mat -> BlockingKing/BoxGlass -> clone -> SetColor -> DrawMeshInstanced

GridOverlay PathFlow:
  BlockingKing/GridOverlay/PathFlow -> shader registry -> Always Included
```

这个组合目前能满足：

```text
单位正常绘制
数字正常绘制
PathFlow 正常绘制
Box 外扩玻璃正常绘制
Lit 变体数量维持在合理范围
```

