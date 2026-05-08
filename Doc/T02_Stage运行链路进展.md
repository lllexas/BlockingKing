# Stage 运行链路进展

日期：2026-05-08

## 今日完成

### 1. Stage ECS 基础链路

当前关卡运行入口仍然由 `LevelPlayer` 负责。`LevelPlayer.Start()` 会解析关卡数据、重建地形 Mesh，并初始化 ECS。

运行链路：

```text
LevelPlayer.Start()
  -> ResolveLevelData()
  -> BuildMesh()
  -> BuildEntities()
```

`BuildEntities()` 会确保运行时系统存在，并初始化实体数据：

```text
EntitySystem
IntentSystem
MoveSystem
AttackSystem
DrawSystem
UserInputReader
```

### 2. 输入与 Intent

新增 `UserInputReader`，当前只监听 WASD。

输入链路：

```text
UserInputReader.Update()
  -> 监听 WASD
  -> 找到 Player 实体
  -> 申请 MoveIntent
  -> 设置方向与距离 Distance=1
  -> 写入玩家 IntentComponent
  -> TickSystem.PushTick()
```

### 3. Tick 分发

当前只有 `EntitySystem` 订阅 `TickSystem.OnTick`。

```text
TickSystem.PushTick()
  -> EntitySystem.UpdateTick()
     -> IntentSystem.Tick()
```

`MoveSystem` 和 `AttackSystem` 不再作为 Tick 系统运行，只作为具体规则执行系统被调用。

### 4. 移动与推箱

`IntentSystem.Tick()` 消费实体 Intent，并将 `MoveIntent` 转交给 `MoveSystem.Execute()`。

`MoveSystem` 当前支持逐步移动和推箱：

```text
人 箱 空  -> 空 人 箱
人 箱 墙  -> 不动
人 箱 箱 空 -> 默认不动
```

`MoveSystem` 暴露 `maxPushChain`，默认值为 `1`。后续如果需要升级能力，可以调高该值来支持连续推动多个箱子。

### 5. 绘制

新增 `DrawSystem`，不参与逻辑 Tick。

当前绘制策略：

```text
Player -> Capsule
Box    -> Cube
```

绘制使用 `Graphics.DrawMeshInstanced`。实体绘制位置已经对齐到格子中心：

```text
world = (x + 0.5, y, z + 0.5)
```

### 6. 墙体策略

墙体采用“静态地形 + 按需实体化”的方案。

普通墙：

```text
存在 groundMap
由 TileMappingConfig.entries[].isWall 标记阻挡
不进入 EntitySystem 实体列表
```

需要被交互或破坏时：

```text
EntitySystem.TryMaterializeWall(pos)
  -> 创建 EntityType.Wall
  -> 占用 gridMap
  -> groundMap[pos] 改成默认地板
```

移动阻挡查询统一走：

```text
EntitySystem.IsBlocked(pos)
```

其判断顺序：

```text
越界 -> 阻挡
gridMap 有实体 -> 阻挡
groundMap 是 wall terrain -> 阻挡
```

### 7. LevelPlayer 操作

`LevelPlayer` 增加了 `RestartLevel()` 按钮。

```text
RestartLevel()
  -> ResolveLevelData()
  -> BuildMesh()
  -> BuildEntities()
```

用于运行期或编辑期快速重置关卡状态。

## 当前待处理

- `AttackSystem` 仍是空壳，尚未接入攻击墙体、伤害和销毁。
- 墙体实体化后，地形 Mesh 目前不会自动局部刷新。
- `DrawSystem` 目前只绘制 Player 和 Box，尚未绘制 Wall/Enemy/Target 实体。
- `MoveSystem` 目前只允许推动 `EntityType.Box`。
- `LevelPlayer` 仍承担较多启动职责，后续可以再拆出更明确的 Stage 启动入口。
