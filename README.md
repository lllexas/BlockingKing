# BlockingKing

一个以推箱子为核心空间规则的 3D 肉鸽原型。

当前项目已经接入：

- Classic / Escort 双主战斗模式
- 敌人 intent 节拍执行
- 手牌与卡牌释放
- RoundFlow 肉鸽流程
- 商店、事件、奖励、结算等 run 基础系统

项目状态：

- 可玩原型
- 持续施工中
- 系统层已经成型，内容与体验仍在快速迭代

## 快速开始

Unity 版本：

- `2022.3.57f1c2`

主要场景：

- `Assets/Scenes/StageScene.unity`
- `Assets/Scenes/SampleScene.unity`
- `Assets/Scenes/TilemapLevelEditor.unity`

推荐入口：

- 日常运行 / 主游戏流程：`StageScene.unity`
- TileMap 编辑：`TilemapLevelEditor.unity`

项目主流程由 `GameFlowController` 驱动，支持这些模式：

- `DirectLevel`
- `RouteMap`
- `RoundFlow`
- `Tutorial`
- `LevelEdit`

当前最重要的正式入口是：

- `RoundFlow`
- `Tutorial`

## 运行说明

### 1. 跑一局 RoundFlow

打开：

- `Assets/Scenes/StageScene.unity`

确认场景中的 `GameFlowController` 已正确挂载：

- `RunConfig`
- `RunRoundConfig`
- 相关 UI / runtime 系统

`GameFlowController` 会通过 `RunConfig` 把整套 run 配置串起来。

主配置入口：

- `Assets/Settings/RunConfig/RunConfig.asset`

### 2. 直接测关卡

如果要直接测单关逻辑，核心入口是：

- `LevelPlayer`

它负责：

- 读取 `LevelData`
- 构建 ECS 世界
- 驱动战斗 / intent / draw / 结算

### 3. 教程与编辑模式

`GameFlowController` 同时支持：

- `Tutorial`
- `LevelEdit`

教程模式依赖：

- `tutorialLevel`
- `tutorialTileConfig`

编辑模式依赖：

- `LevelEditSession`

## 仓库结构

### 代码

- `Assets/Scripts`

主要系统分布：

- `GameFlowController`
  - 总流程入口
- `RunRoundController`
  - RoundFlow 肉鸽循环
- `RunRouteFacade`
  - 大地图 / 路线生成与节点推进
- `LevelPlayer`
  - 单关运行、ECS 构建、战斗结算
- `HandZone`
  - 手牌、出牌、卡牌交互
- `Assets/Scripts/Stage`
  - intent / entity / move / attack / draw / overlay 等战斗底层系统

### 配置

- `Assets/Settings/RunConfig`

这里是当前最重要的 run 配置入口。

推荐优先认识这些资产：

- `Assets/Settings/RunConfig/RunConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunRoundConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficultyConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunRewardConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/ClassicLevelFeatureSelectionTable.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/EscortLevelFeatureSelectionTable.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnDifficultyProfile.Default.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnTimingProfile.asset`

### 文档

- `Plan/`

这里放当前施工中的设计文档、优先级清单和系统说明。

## 核心系统概览

### 1. Run 配置分层

当前 run 配置大致按这几层拆分：

- `RunConfig`
  - 总装配层
- `RunRoundConfig`
  - 一局流程结构
- `LevelFeatureSelectionTable + LevelFeatureFilter`
  - 关卡段落与地图筛选
- `RunDifficultyConfig`
  - 数值曲线
- `EnemySpawnDifficultyProfile`
  - 敌人构成
- `EnemySpawnTimingProfile`
  - 刷怪节奏
- `RunRewardConfig`
  - 奖励与经济

### 2. 战斗系统

战斗部分当前基于 ECS 风格数据组织，关键模块包括：

- `EntitySystem`
- `IntentSystem`
- `EnemyAutoAISystem`
- `MoveSystem`
- `AttackSystem`
- `CardEffectSystem`
- `DrawSystem`
- `GridOverlayDrawSystem`

战斗演出和输入节拍目前已经和 intent 队列打通。

### 3. UI 与面板

当前项目同时存在两类 UI：

- `SpaceUIAnimator` 体系下的正式面板
- 还没完全替换掉的部分 OnGUI / 过渡前端

主要 run 面板已经逐步迁入正式体系，例如：

- `RunRoundBackdropPanelAnimator`
- `RunDeckPanelAnimator`
- `RunShopPanelAnimator`
- `RunCombatSettlementPanelAnimator`
- `RunResultPanelAnimator`

## 配置入口

如果你是程序：

- 先看 `GameFlowController`
- 再看 `RunRoundController`
- 再看 `LevelPlayer`

如果你是策划：

- 先看 `Assets/Settings/RunConfig/RunConfig.asset`
- 再看 `Plan/` 里的配置文档

## 文档导航

当前建议先读这几份：

- [Plan/肉鸽难度曲线与Round配置_操作指南.md](</G:/ProjectOfGame/BlockingKing/Plan/肉鸽难度曲线与Round配置_操作指南.md>)
- [Plan/肉鸽难度曲线与Round配置_设计原理说明.md](</G:/ProjectOfGame/BlockingKing/Plan/肉鸽难度曲线与Round配置_设计原理说明.md>)
- [Plan/2026-05-13_下午_剩余需求统计.md](</G:/ProjectOfGame/BlockingKing/Plan/2026-05-13_下午_剩余需求统计.md>)
- [Plan/推箱子肉鸽设计方案.md](</G:/ProjectOfGame/BlockingKing/Plan/推箱子肉鸽设计方案.md>)

## 当前开发状态

目前已经基本接上的部分：

- run 基础流程
- Classic / Escort 主战斗模式
- intent 节拍展示与执行
- 手牌 / 卡牌释放
- 商店 / 事件 / 奖励 / 战斗结算
- 单位死亡动画

仍在持续迭代的部分：

- 教程
- 难度曲线收束
- 炮兵逻辑优化
- 事件表现与内容
- 遗物 / 藏品实际效果系统

## 备注

这是一个快速迭代中的原型仓库。

README 的职责是提供入口，不替代 `Plan/` 里的专题文档。
