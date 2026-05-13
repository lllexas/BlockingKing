# 当前 UI 系统 - 技术策划 Demo

## 一句话定位

当前 UI 系统围绕 `SpaceUIAnimator` 搭建，用事件请求驱动不同面板显示和隐藏，服务主菜单、RoundFlow 肉鸽流程、战斗手牌、商店、结算、设置、唱片机和教程提示。

它的目标不是做最终商业 UI，而是在一周 demo 内建立一套可运行、可切状态、可继续扩展的产品级 UI 骨架。

## UI 架构原则

当前 UI 的核心原则是：

```text
流程状态由 GameFlowController / RunRoundController 管
UI 显示由 SpaceUIAnimator 子类执行
面板切换通过 PostSystem 的“期望显示面板 / 期望隐藏面板”事件驱动
具体面板通过 UIID 匹配自己的请求
```

这种做法让 UI 不直接写死在流程代码里。流程只广播“希望显示哪个面板”，具体动效、文本刷新和按钮绑定由对应 Animator 负责。

## SpaceUIAnimator 子类总览

当前主要 UI Animator：

```text
主菜单
  MainMenuBackdropAnimator
  MainMenuTitleAnimator
  MainMenuStartAnimator
  MainMenuTutorialAnimator
  MainMenuSettingsAnimator
  MainMenuQuitAnimator

RoundFlow
  RunRoundBackdropPanelAnimator
  RunRoundHudPanelAnimator
  RunRoundClassicChoicePanelAnimator
  RunRoundEscortChoicePanelAnimator
  RunRoundSkipChoicePanelAnimator
  RunCombatSettlementPanelAnimator
  RunRoundShopChoicePanelAnimator
  RunRoundEventChoicePanelAnimator
  RunResultPanelAnimator

战斗与卡牌
  HandZoneAnimator
  CardImpactAnimator
  DrawPilePanelAnimator
  DiscardPilePanelAnimator
  RunDeckPanelAnimator

商店
  RunShopPanelAnimator

全局功能
  BgmRecordAnimator
  RunSettingsPanelAnimator
  RunSettingsRestartAnimator
  RunSettingsQuitAnimator

教程
  TutorialPromptPanelAnimator
```

## 主菜单 UI

主菜单由 `MainMenuUIIds` 管理：

```text
MainMenu.Backdrop
MainMenu.Title
MainMenu.Start
MainMenu.Tutorial
MainMenu.Settings
MainMenu.Quit
```

对应 Animator：

```text
MainMenuBackdropAnimator
MainMenuTitleAnimator
MainMenuStartAnimator
MainMenuTutorialAnimator
MainMenuSettingsAnimator
MainMenuQuitAnimator
```

当前主菜单承担：

```text
展示项目标题 Blocking King
展示当前 RunConfig 名称
开始 RoundFlow
进入 Tutorial
打开设置面板
退出游戏
```

主菜单由 `GameFlowController.ShowMainMenuRound()` 控制显示。

主菜单和 Run 内 UI 的生命周期已经拆开：返回主菜单时会隐藏 Run 面板、锁手牌、切回主菜单音乐。

## RoundFlow UI

RoundFlow UI 由 `RunRoundUIStateRegistry` 按 `RunRoundState` 统一调度。

状态与面板关系：

```text
RoundOffer
  RunRoundBackdrop
  RunRoundHud
  RunRoundClassicChoice
  RunRoundEscortChoice
  RunRoundSkipChoice

CombatSettlement
  RunRoundBackdrop
  RunRoundHud
  RunCombatSettlement

PostCombatOffer
  RunRoundBackdrop
  RunRoundHud
  RunRoundShopChoice
  RunRoundEventChoice

Shop
  RunRoundBackdrop
  RunRoundHud

Event
  RunRoundBackdrop
  RunRoundHud

EventStage
  RunRoundBackdrop

Defeat / RunComplete
  RunRoundBackdrop
  RunResult
```

这种状态表让 UI 切换规则集中在一处，不需要每个按钮各自隐藏一堆面板。

## 主战斗选择面板

主战斗选择由三个面板组成：

```text
RunRoundClassicChoicePanelAnimator
RunRoundEscortChoicePanelAnimator
RunRoundSkipChoicePanelAnimator
```

它们继承同一个基类：

```text
RunRoundChoicePanelAnimatorBase<TRequest>
```

统一处理：

```text
标题文本
正文文本
脚注文本
按钮绑定
可交互状态
淡入淡出
```

三个选择分别对应：

```text
Classic：进入经典推箱子关
Escort：进入护送关
Skip：放弃本轮并获得补偿奖励
```

UI 文案由 `RunRoundUIStateRegistry` 根据 `RunRoundController.CurrentOffer` 生成。

## HUD

HUD 对应：

```text
RunRoundHudPanelAnimator
```

当前显示：

```text
Round 进度
Encounter Cycle 进度
HP
金币
状态提示
```

HUD 会在可见期间持续刷新，因此金币、血量这类运行时数据可以随 Facade 状态更新。

## 战斗结算 UI

战斗结算对应：

```text
RunCombatSettlementPanelAnimator
```

它会按 beat 节奏逐段揭示：

```text
结算标题
关卡类型和关卡名
成功归位箱子数量
奖励明细
金币总计
HP 状态
结算提示
继续按钮
```

如果玩家点击时结算还在逐段显示，会先跳到完整显示；再次点击才进入下一步。这保证了“可看演出”和“可快速跳过”两种需求。

## 战后选择 UI

战后选择包括：

```text
RunRoundShopChoicePanelAnimator
RunRoundEventChoicePanelAnimator
```

它们同样复用 `RunRoundChoicePanelAnimatorBase<TRequest>`。

当前表达：

```text
Shop：进入商店，购买卡牌或道具
Event：进入随机事件或获得即时事件奖励
```

这部分对应 `RunRoundState.PostCombatOffer`。

## Run 结果 UI

Run 结束面板对应：

```text
RunResultPanelAnimator
```

根据 `RunResultUIRequest.Victory` 显示：

```text
Run Complete
Run Failed
```

按钮可返回主菜单。

## 手牌与卡牌 UI

战斗中的手牌主体逻辑在 `HandZone` 和 `CardView`，UI 动效辅助由：

```text
HandZoneAnimator
CardImpactAnimator
DrawPilePanelAnimator
DiscardPilePanelAnimator
```

当前支持：

```text
手牌显示
卡牌 hover 展开
拖拽出牌
Pending 瞄准
左键确认释放
右键取消
抽牌堆 / 弃牌堆数量显示
卡牌 draw / hover / press / aim / release 动效
```

`CardImpactAnimator` 不拦截 Raycast，只负责卡牌视觉冲击动效，避免影响 `CardView` 的拖拽和点击。

## 牌组面板

牌组查看对应：

```text
RunDeckPanelAnimator
```

它从 `CardDeckFacade` 读取当前牌组，实例化 `CardView` 列表，并支持滚动查看。

当前用途：

```text
查看当前构筑
确认奖励或商店购买后的牌组变化
提供后续遗物、删牌、升级等系统的 UI 扩展入口
```

## 商店 UI

商店面板对应：

```text
RunShopPanelAnimator
```

当前支持：

```text
显示商店标题
显示当前金币
展示卡牌商品
展示道具商品
选择商品查看描述
购买商品
售罄状态
离开商店
```

商店可以由两种路径打开：

```text
RoundFlow 战后 Shop 选择
NekoGraph 事件 Pack 中的 Shop 节点
```

商品购买会写入：

```text
CardDeckFacade
RunInventoryFacade
```

## 设置 UI

设置面板对应：

```text
RunSettingsPanelAnimator
RunSettingsRestartAnimator
RunSettingsQuitAnimator
```

当前支持：

```text
主音量
音效音量
音乐音量
返回主菜单
重新开始
退出游戏
```

音量通过 `AudioBus` 生效。

设置面板显示时，唱片机按钮会隐藏，避免全局按钮互相抢占操作焦点。

## BGM 唱片机 UI

BGM 唱片机对应：

```text
BgmRecordAnimator
```

当前支持：

```text
显示当前曲名
显示 BPM
点击切换下一首
hover 缩放反馈
idle 呼吸动效
```

它会根据流程自动隐藏：

```text
主菜单显示时隐藏
LevelEdit 关卡编辑模式隐藏
设置面板打开时隐藏
```

这样可以避免编辑器、主菜单和运行中 UI 混在一起。

## 教程提示 UI

教程提示对应：

```text
TutorialPromptPanelAnimator
```

当前支持：

```text
标题
正文提示
淡入淡出
```

它由 `TutorialUIIds.Prompt` 识别，用于教程步骤中的提示文本。

## 过渡与保留 UI

当前工程中仍有部分 OnGUI / 过渡前端，例如：

```text
RunRouteOnGUIFrontend
RunShopOnGUIFrontend
BgmRecordOnGUIFrontend
RunSettingsOnGUIFrontend
```

正式 Run 面板正在逐步迁入 `SpaceUIAnimator` 体系。

这也是当前 UI 的真实状态：主流程核心面板已经可用，但仍保留部分过渡实现以保证功能完整。

## 对技术策划能力的体现

当前 UI 系统体现的重点是：

```text
用状态机管理 UI，而不是靠按钮散写显示隐藏
把主菜单、Run、战斗、结算、商店、设置拆成独立面板
让 UI 能响应配置和运行时数据
保留可跳过的结算演出节奏
把手牌交互、卡牌动效和牌堆计数分层
把音乐控制做成全局按钮，但按流程自动隐藏
承认过渡 UI 的存在，并明确后续迁移方向
```

## 当前限制

```text
视觉资源仍以临时版本为主
部分 OnGUI 前端尚未完全替换
UI 样式还没有完全统一
缺少统一的 UI 截图验收表
部分面板还依赖场景中手动绑定的引用
商店和牌组面板的布局还需要更多分辨率验证
```

## 后续方向

```text
统一所有 Run 面板到 SpaceUIAnimator
补齐 UI 截图验收清单
整理按钮、标题、正文、脚注的统一样式
商店增加商品稀有度和售罄表现
牌组面板增加筛选、排序、卡牌详情
结算面板增加奖励图标
教程提示增加步骤状态和确认按钮
```

## 相关文档

- [卡牌前端维护文档](../T04_卡牌前端维护文档.md)
- [主菜单与 Run 态收束日志](../T11_主菜单与Run态收束日志.md)
- [RunConfig 技术策划 Demo](肉鸽进程配置工具_RunConfig_技术策划Demo.md)

