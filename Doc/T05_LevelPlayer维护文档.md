# T05 LevelPlayer 维护文档

## 目标

`LevelPlayer` 是关卡运行入口，负责把 `LevelData` 和 `TileMappingConfig` 装载成可玩的场景，并按指定模式推进结算。

维护时要把它当作一个有生命周期的系统，而不是依赖 `Start()` 的一次性脚本。

当前生命周期：

```text
LoadLevel -> RebuildWorld -> StartPlayback -> StopPlayback
```

## 核心数据

`LevelData` 提供地图、tag 坐标、tagID。

`TileMappingConfig` 提供 tagID 的语义映射，例如 `Player`、`Box`、`Target`。没有 config 时会使用项目默认 fallback：

```text
Player = 1
Box = 2
Target = 3
```

不要把 `LevelData` 单独视为完整播放输入。正确的运行时入口应传 `LevelPlayRequest`。

## 公开入口

### `PlayLevel(LevelPlayRequest request)`

route、stage、其它外部系统应优先使用这个入口。

它会按顺序执行：

```text
StopPlayback
LoadLevel
RebuildWorld
StartPlayback
```

返回 `bool`。调用方必须只在返回 `true` 后进入后续流程，例如 route 节点开始状态。

### 兼容入口

以下入口仍可用，但只是兼容层：

```csharp
PlayLevel(LevelData level)
PlayLevel(LevelData level, LevelPlayMode mode)
PlayLevel(LevelData level, LevelPlayMode mode, int maxSteps)
```

它们内部会组装 `LevelPlayRequest`，并使用 `LevelPlayer` Inspector 上的 `tileConfig`。

新系统不要优先使用这些重载。

### 结算结果

关卡结算使用 `LevelPlayResult`：

```text
None       未结算
Success    成功完成关卡
Failure    失败结算，例如 StepLimit 步数归零
Cancelled  取消播放，通常只清状态，不推进流程
```

规则对象只应调用：

```csharp
SettleLevel(LevelPlayResult result, string reason)
```

不要再使用 `bool success` 表达结算。route 下游必须能区分成功、失败和取消。

### `LoadConfiguredLevel()`

只服务直接场景播放和 Inspector 工作流。优先级：

```text
QuickPlaySession -> Inspector levelData
```

route 不应该走这个入口。

### `RebuildWorld()`

只重建当前已加载关卡：

```text
BuildMeshInternal
BuildEntitiesInternal
CacheTargetCells
```

它不应该决定关卡来源。

### `BuildMesh()` / `BuildEntities()`

这两个方法主要是 Inspector 按钮和调试兼容入口。若没有当前关卡，会尝试 `LoadConfiguredLevel()`。

运行时完整切关不要直接调用它们，使用 `PlayLevel(LevelPlayRequest)`。

## 播放模式

播放模式由 `LevelPlayMode` 和内部规则对象控制：

```csharp
private interface ILevelPlayRule
{
    void Begin(LevelPlayer player, LevelPlayRequest request);
    void OnTick(LevelPlayer player);
    void Evaluate(LevelPlayer player);
}
```

现有模式：

| 模式 | 规则 |
| --- | --- |
| `Classic` | 所有箱子在目标点上时成功结算 |
| `StepLimit` | 每次 `TickSystem.OnTick` 剩余步数减一；提前完成则成功，步数归零则失败 |

`StepLimit` 规则顺序：

```text
Begin      设置 RemainingSteps
OnTick     RemainingSteps -= 1
Evaluate   先判断箱子到位成功，再判断步数归零失败
```

因此最后一步把箱子推到目标点时，即使步数同时归零，也按成功结算。

新增模式步骤：

1. 在 `LevelPlayMode` 加枚举值。
2. 新增一个实现 `ILevelPlayRule` 的规则类。
3. 在 `CreatePlayRule(LevelPlayMode mode)` 中返回新规则。
4. 如果模式需要参数，把参数加到 `LevelPlayRequest`，不要从 `LevelPlayer` 的 Inspector 字段偷读。

## Route 调用约定

`RunRouteFacade` 应解析出 `LevelData` 后调用：

```csharp
bool started = player.PlayLevel(new LevelPlayRequest
{
    Level = level,
    Mode = ResolveLevelPlayMode(side.stageType),
    StepLimit = ResolveStepLimit(side.stageType, 30)
});
```

只有 `started == true` 后，才允许：

```text
BeginRouteNode
OnRouteClassicLevelStarted
```

这样避免 route 状态已经开始，但 `LevelPlayer` 实际没有加载成功的半启动状态。

经典关卡结算后，`GameFlowController.OnRouteClassicLevelSettled(result)` 负责分发：

```text
Success -> CompleteActiveRouteNode -> MarkNodeCompleted
Failure -> FailActiveRouteNode -> 只清 ActiveRouteNode，不标记完成
Cancelled -> StopPlayback，不应推进 route
```

`StepLimit` 步数归零是 `Failure`，不会默认完成 route 节点。

`stageType` 当前约定：

```text
空或其它值       -> Classic
steplimit        -> StepLimit, 默认 30 步
steplimit:50     -> StepLimit, 50 步
step_limit:50    -> StepLimit, 50 步
step-limit:50    -> StepLimit, 50 步
```

## 清理规则

## 地形渲染 B 版

`LevelPlayer` 默认使用 `useInstancedTerrain`：

```text
BuildEntitiesInternal
CacheTargetCells
RebuildTerrainVisualsInternal
```

B 版不生成整图 `LevelMesh`，而是由 `TerrainDrawSystem` 读取 `EntitySystem.entities.groundMap`，按类型批量绘制：

```text
Floor mesh  -> floor matrices
Wall mesh   -> wall matrices
Target mesh -> target marker matrices
```

绘制方式是 `Graphics.DrawMeshInstanced`，不是每格一个 GameObject。

`EntitySystem.TerrainVersion` 是地形变化版本号。以下操作会递增版本：

```text
Initialize
ClearWorld
SetTerrain(pos, terrainId)
SetTerrain(map)
TryMaterializeWall
```

`TerrainDrawSystem` 每帧检查版本号，变化时重建 matrix 缓存。因此破坏地形或把墙体实体化后，不需要重建整张 mesh。

旧 `LevelMeshBuilder` 保留为 fallback。关闭 `useInstancedTerrain` 时会禁用 `TerrainDrawSystem` 并恢复整图 mesh 构建。

### Mesh

不要只依赖 `_meshGO` 引用清理旧地图。热重载、重复播放、旧流程残留都可能让字段引用丢失。

当前清理策略是删除 `LevelPlayer` 子物体下所有名为 `LevelMesh` 的对象。

### ECS

实体数据由 `EntitySystem.Initialize(...)` 重建。该方法会重置 `entityCount`、组件数组、`gridMap` 和 `groundMap`。

如果未来引入非 ECS 的运行时可视物，必须在 `RebuildWorld()` 中明确清理。

### Tick

`StartPlayback()` 订阅 `TickSystem.OnTick`。

`StopPlayback()` 和 `OnDestroy()` 必须取消订阅。

不要在模式规则里直接订阅全局 tick。

## 常见坑

### 1. `LevelData` 不是完整输入

单位生成依赖 `TileMappingConfig`。如果只传 `LevelData`，tagID 可能被错误解释。

### 2. 构建函数不应决定关卡来源

`BuildMeshInternal()` 和 `BuildEntitiesInternal()` 只使用当前已加载的 `_level/_config`。

不要在内部重新读取 Inspector 字段，否则 route 播放可能被旧字段覆盖。

### 3. fallback 只能兜底，不能替代 config

fallback ID 是为了避免空 config 直接崩溃，不是正常配置路径。看到 `TileMappingConfig is missing` 日志应修配置。

### 4. 结算只通过规则触发

不要在输入、移动、绘制系统里直接判定关卡完成。关卡完成条件属于 `ILevelPlayRule`。

## 推荐调试日志

进入关卡时应能看到：

```text
[LevelPlayer] Level loaded: ..., source=RuntimeRequest
[LevelPlayer] Playback started: ..., mode=...
```

如果 route 播放时看到 `source=Inspector`，说明没有走 `PlayLevel(LevelPlayRequest)` 或 route 没有解析到目标 `LevelData`。

## 维护原则

- 新入口必须显式传完整 `LevelPlayRequest`。
- 新模式只加规则，不改 `Start()`。
- `Start()` 只服务直接场景播放。
- `RebuildWorld()` 只重建当前关卡，不选择关卡。
- route 状态必须在 `PlayLevel` 成功后再推进。
