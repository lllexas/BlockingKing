# BlockingKing

一个以推箱子为核心规则的 Unity 3D 卡牌肉鸽 Demo。

## 快速体验

- [BlockingKing Releases](https://github.com/lllexas/BlockingKing/releases)

推荐 Unity 版本：

```text
Unity 2022.3 LTS
当前工程版本：2022.3.57f1c2
```

## “产品标准”

本项目不是只实现一个推箱子规则原型，而是按“可以被玩家打开、理解、游玩、结束，并能被策划继续生产内容”的标准收束。

当前已经完成的产品闭环：

```text
主菜单入口
进入一局 Run
选择 Classic / Escort / Skip
进入关卡
推箱、战斗、出牌、悔棋
结算奖励
进入 Shop / Event
继续下一轮
最终形成一局完整体验
```

当前已经完成的生产闭环：

```text
用 3D 关卡编辑器编辑 LevelData
保存关卡
通过 RunConfig 配置关卡池、奖励、难度、商店、事件、BGM
进入 Run 中验证
再回到编辑器和配置继续调整
```

## 核心玩法

BlockingKing 的目标不是把推箱子做得更难，而是降低推箱子的畏难感。

传统推箱子容易让玩家担心：

```text
一步错，可能全盘错
不知道什么时候已经死局
重开会让前面的思考白费
高难谜题容易直接劝退
```

本项目通过以下系统承接这些压力：

- `Classic`：先纯推箱拿高收益，再进入卡牌 + 战斗兜底阶段
- `Escort`：护送核心箱，在隐式回合压力下推进路线
- `卡牌`：用战车、主教、骑士、火炮等棋子式能力修正局面
- `悔棋`：允许退一步，但要付递增金币或血量代价
- `Skip`：不想打当前局面时，可以跳过拿低收益保底
- `Shop / Event`：战后补强和资源交换
- `节拍与音乐`：玩家和敌人的回合动作与 BGM 速度隐式对齐，降低连续解谜的干硬感

当前主要关卡体验：

```text
Classic：经典推箱子关，分为纯推箱预结算阶段和卡牌战斗完成阶段
Escort：护送关，围绕核心箱、敌人压力、位移护盾和路线推进展开
```

更完整的设计说明见：

- [Gameplay 设计原理：降低推箱子畏难感的 3D 肉鸽方案](Doc/Demo/Gameplay设计原理_技术策划Demo.md)

## 快速入口

建议按这个顺序阅读：

1. [Gameplay 设计原理](Doc/Demo/Gameplay设计原理_技术策划Demo.md)
2. [3D 关卡编辑器：面向策划的实时关卡生产工具](Doc/Demo/3D关卡编辑器_技术策划Demo.md)
3. [肉鸽进程配置工具：RunConfig 驱动的局内流程配置](Doc/Demo/肉鸽进程配置工具_RunConfig_技术策划Demo.md)
4. [当前 UI 系统：主菜单、Run 面板、手牌、商店与结算](Doc/Demo/当前UI系统_技术策划Demo.md)

其中：

- `Gameplay` 说明为什么这套玩法按产品体验成立。
- `3D 关卡编辑器` 对应题目中的“关卡编辑器”要求。
- `RunConfig` 说明如何用配置驱动一局完整 Run。
- `当前 UI 系统` 说明玩家入口、局内 HUD、手牌、商店、结算等界面。

## 工程运行

1. 使用 Unity 打开仓库根目录。
2. 打开主场景：

```text
Assets/Scenes/StageScene.unity
```

3. 确认场景中的 `GameFlowController` 使用当前 Run 配置：

```text
Assets/Settings/RunConfig/RunConfig.asset
```

4. 确认场景中的 `GameFlowController` 的 Mode 字段和 Route Map Startup Mode 字段枚举分别为：

```text
RoundFlow
Main Menu Round
```

## 运行环境

- Unity：`2022.3.57f1c2`
- 渲染管线：URP
- 主要依赖：Odin Inspector、DOTween、TextMesh Pro

Unity 内建议直接打开 `StageScene.unity` 后进入 Play Mode。

## 关卡编辑器

项目包含 Play Mode 3D 关卡编辑器。

入口：

```text
在 Project 右键菜单栏 BlockingKing 内创建或选中 LevelData
  -> Inspector 点击「3D 编辑」
  -> 进入 GameFlowMode.LevelEdit
```

编辑器能力：

- 在真实 3D 运行场景中编辑 `LevelData`
- 从 `TileMappingConfig` 自动生成 terrain / tag palette
- 支持刷地形、刷 tag、擦除、撤销、重做、保存、放弃
- 每次编辑后通过 `LevelPlayer` 重建运行数据与 3D 地形
- 退出 Play Mode 后恢复原 GameFlow 状态

详细说明：

- [3D 关卡编辑器 Demo 文档](Doc/Demo/3D关卡编辑器_技术策划Demo.md)
- [3D 关卡编辑器使用指南](Doc/T14_3D关卡编辑器使用指南.md)

## Run 配置

当前肉鸽流程由 `RunConfigSO` 作为总入口：

```text
Assets/Settings/RunConfig/RunConfig.asset
```

配置分层：

- `RunStartSettings`：开局卡组、金币、生命值、手牌规则
- `RunRoundConfigSO`：一局 Run 的主循环、Classic / Escort / Skip、Shop / Event
- `RunDifficultyConfigSO`：敌人血量、攻击、奖励倍率和刷怪节奏
- `RunRewardConfigSO`：Classic / Escort 奖励结算
- BGM 配置：主菜单音乐和 Run 内音乐

说明：

- 当前正式主流程是 `RoundFlow`
- `RunRouteConfigSO` 是早期路线图原型，目前不作为主流程

详细说明：

- [RunConfig 技术策划 Demo 文档](Doc/Demo/肉鸽进程配置工具_RunConfig_技术策划Demo.md)
- [RunConfig 内部说明](Doc/T16_肉鸽进程配置工具_RunConfig说明.md)

## 主要场景

```text
Assets/Scenes/StageScene.unity
  主运行场景，推荐评审时优先打开

Assets/Scenes/SampleScene.unity
  备用测试场景

Assets/Scenes/TilemapLevelEditor.unity
  早期 Tilemap 编辑相关场景
```

## 主要代码入口

```text
Assets/Scripts/GameFlowController.cs
  总流程入口，负责 DirectLevel / RoundFlow / Tutorial / LevelEdit 等模式切换

Assets/Scripts/RunRoundController.cs
  RoundFlow 肉鸽循环

Assets/Scripts/LevelPlayer.cs
  单关加载、运行数据构建、关卡运行、战斗结算

Assets/Scripts/Level3DEditorController.cs
  3D 关卡编辑器的 Play Mode 交互

Assets/Scripts/LevelDataInspector.cs
  LevelData Inspector 入口、保存、放弃、退出和状态恢复

Assets/Scripts/Stage/
  Entity / Intent / Move / Attack / Draw / Overlay 等关卡运行系统
```

## 主要资源入口

```text
Assets/Settings/RunConfig/
  当前 Run 配置

Assets/Settings/TileMappingConfig.asset
  tile / tag 到运行时表现和实体语义的映射

Assets/Resources/
  运行时可加载资源

Assets/Scenes/
  场景入口
```

## 文档导航

- [Gameplay 设计原理：降低推箱子畏难感的 3D 肉鸽方案](Doc/Demo/Gameplay设计原理_技术策划Demo.md)
- [3D 关卡编辑器：面向策划的实时关卡生产工具](Doc/Demo/3D关卡编辑器_技术策划Demo.md)
- [肉鸽进程配置工具：RunConfig 驱动的局内流程配置](Doc/Demo/肉鸽进程配置工具_RunConfig_技术策划Demo.md)
- [当前 UI 系统：主菜单、Run 面板、手牌、商店与结算](Doc/Demo/当前UI系统_技术策划Demo.md)

维护细节：

- [T13 3D 关卡编辑器维护指南](Doc/T13_3D关卡编辑器维护指南.md)
- [T14 3D 关卡编辑器使用指南](Doc/T14_3D关卡编辑器使用指南.md)
- [T15 3D 关卡编辑器后续开发指南](Doc/T15_3D关卡编辑器后续开发指南.md)
- [T16 肉鸽进程配置工具 RunConfig 说明](Doc/T16_肉鸽进程配置工具_RunConfig说明.md)

设计资料：

- [关卡设计纲要](Doc/Design/01_关卡设计.md)
- [卡牌设计](Doc/Design/03_卡牌设计.md)
- [怪物设计](Doc/Design/04_怪物设计.md)

## 当前状态

这是一个一周周期内完成的可玩产品级 Demo。

已经完成的重点是：

- 推箱子核心规则可运行
- 单关体验链路可运行
- 肉鸽 Run 流程可运行
- 关卡编辑器可进入、可编辑、可保存
- Release 可直接下载体验
- 配置资产能够驱动开局、循环、难度、奖励和 BGM

可继续扩展的方向：

- 教程体验打磨
- 数值曲线收束
- 事件内容扩展
- UI 表现统一
- 关卡编辑器验证面板和高级编辑模式
