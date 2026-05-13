# 3D 关卡编辑器 - 技术策划 Demo

## 一句话定位

3D 关卡编辑器是一个面向关卡策划的实时编辑工具：策划可以在 Unity Play Mode 中直接编辑 `LevelData`，并立即通过真实的 `LevelPlayer`、ECS 和 3D 地形显示看到运行效果。

它不是独立预览器，而是复用正式运行链路的编辑模式。

## 解决的问题

本项目的关卡数据本质是 2D tile/tag，但玩家看到的是 3D 场景和 ECS 实体。

如果只在表格、Inspector 或 2D 数据视图里改关卡，策划很难立即判断：

```text
这个 tile 在 3D 里是否符合预期
这个 tag 是否生成了正确实体
目标点、墙、单位是否和运行时规则一致
关卡在真实摄像机和演出节奏下是否可读
```

3D 关卡编辑器把“编辑”和“运行验证”合并到同一个场景里，减少反复切换数据和场景的成本。

## 策划使用流程

```text
1. 在 Project 中选中一个 LevelData
2. 确认当前场景中有 GameFlowController 和 LevelPlayer
3. 确认 LevelPlayer 使用正确的 TileMappingConfig
4. 在 LevelData Inspector 点击「3D 编辑」
5. Unity 进入 Play Mode，并切到 GameFlowMode.LevelEdit
6. 在左侧面板选择 terrain / tag brush
7. 鼠标刷格、擦除、撤销、重做
8. 观察 ECS 实体和 3D 地形即时刷新
9. 保存 3D 修改，或放弃修改
10. 退出 Play Mode，场景流程状态自动恢复
```

## 当前支持内容

```text
从 LevelData Inspector 进入 3D 编辑
进入 Play Mode 后隐藏正式 Run UI
从 TileMappingConfig 自动生成 palette
支持 terrain 和 tag brush
支持分类、搜索、滚动列表
支持鼠标左键刷入
支持鼠标右键擦除
支持 Ctrl+Z / Ctrl+Y 撤销重做
支持 Ctrl+S 保存
支持 Esc 或 Unity Stop 退出
刷格后立即重建 LevelPlayer 世界
保存写回原始 LevelData
放弃恢复到保存前数据
退出后恢复 GameFlowController.mode
退出后恢复 LevelPlayer.levelData
```

## 关键设计取舍

### 1. 复用正式运行链路

编辑器没有单独做一套 3D 预览场景，而是复用：

```text
GameFlowController
LevelPlayer
CameraController
EntitySystem
TerrainDrawSystem
DrawSystem
```

这样做的好处是：策划看到的编辑结果就是正式运行结果，不会出现“编辑器里看起来对，进游戏后不对”的双系统偏差。

### 2. LevelData 和 TileMappingConfig 共同作为数据源

`LevelData` 只保存地图尺寸、地形 ID、tag 坐标。

`TileMappingConfig` 决定这些 ID 和 tag 在运行时如何被解释：

```text
地形语义
显示颜色
编辑 brush
EntityBP
3D 表现
ECS 实体生成规则
```

因此编辑器 palette 不硬编码 tag，而是从 `TileMappingConfig.entries` 和 `TileMappingConfig.tagDefinitions` 生成。

### 3. 当前使用整关重建

每次刷格后，编辑器走：

```text
LevelPlayer.LoadLevel(draft, config)
LevelPlayer.RebuildWorld()
```

它会重新构建 ECS 与 3D 地形。

当前阶段优先保证规则一致和可维护性，不做单格增量更新。大关卡可能有短暂卡顿，但对当前 demo 的关卡规模是可接受的。

### 4. 保存和退出由 LevelData Inspector 承担

保存、放弃、退出的主入口留在 `LevelData` Inspector，而不是临时挂在场景对象上。

这样能让策划始终知道“当前正在编辑哪个 LevelData 资产”，避免 Play Mode 中误操作场景对象。

## 技术链路

```text
LevelDataInspector.Open3DEditor
  -> 记录原 GameFlow mode
  -> 记录原 LevelPlayer.levelData
  -> GameFlowController.mode = LevelEdit
  -> LevelPlayer.levelData = 当前 LevelData
  -> Enter Play Mode

GameFlowController.Start
  -> StartLevelEdit
  -> 隐藏正式 UI
  -> LevelPlayer.LoadConfiguredLevel
  -> Level3DEditorController.Configure

Level3DEditorController.ApplyBrush
  -> 修改 draft
  -> LevelPlayer.LoadLevel(draft, config)
  -> LevelPlayer.RebuildWorld
```

## 对技术策划能力的体现

这部分 demo 体现的不是单纯“做了一个编辑器按钮”，而是工具设计中的几个判断：

```text
知道策划生产关卡时需要即时反馈
知道编辑数据必须和运行解释保持一致
知道工具不能维护第二套运行规则
知道保存、放弃、退出要有明确资产归属
知道 Play Mode 编辑后需要恢复场景状态
知道当前阶段应优先稳定链路，而不是过早优化增量刷新
```

## 当前限制

```text
当前 UI 使用 IMGUI，视觉表现仍偏工具原型
刷格后是整关重建，不是单格增量更新
缺少正式的关卡合法性验证面板
brush 规则还没有完全数据化
缺少批量填充、框选、移动单位等高级编辑模式
```

## 后续扩展

```text
增加关卡验证面板
增加 Actor / Target / Wall 的显式放置规则
增加 Save As，方便基于旧关卡派生新关卡
增加 Rectangle Fill / Flood Fill / Inspect / Move Actor 等模式
为 TileMappingConfig.TagDefinition 增加 icon、description、exclusiveGroup 等编辑器元数据
在大关卡中改为局部增量刷新
```

## 相关文档

- [3D 关卡编辑器维护指南](../T13_3D关卡编辑器维护指南.md)
- [3D 关卡编辑器使用指南](../T14_3D关卡编辑器使用指南.md)
- [3D 关卡编辑器后续开发指南](../T15_3D关卡编辑器后续开发指南.md)

