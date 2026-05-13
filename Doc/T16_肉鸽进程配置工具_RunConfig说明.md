# T16 肉鸽进程配置工具 RunConfig 说明

## 定位

`RunConfigSO` 是当前肉鸽流程的总配置入口。

它的目标不是配置单个关卡，而是配置“一局 Run 如何开始、如何循环、如何成长、如何发奖励、如何进入商店和事件”。

当前有效主线是：

```text
RunConfigSO
  -> RunStartSettings
  -> RunRoundConfigSO
  -> RunDifficultyConfigSO
  -> RunRewardConfigSO
  -> BGM 配置
  -> GameFlowController.RoundFlow
  -> RunRoundController
```

`RunRouteConfigSO` 是早期路线图方案，目前按废案处理。后续文档中提到它时，只作为历史方案或备用原型，不作为当前肉鸽进程配置工具的主要说明对象。

## 设计目标

肉鸽进程配置工具解决的是策划迭代 Run 结构时的成本问题。

没有这套配置时，以下内容容易散落在代码里：

```text
开局卡组、金币、血量
一局 Run 有多少轮
每轮是否出现经典关、护送关、跳过奖励
战斗后是否出现商店或事件
商店卖什么
事件从哪个事件池抽
敌人强度如何随进度增长
奖励金币如何随进度增长
主菜单音乐和 Run 内音乐如何切换
```

现在这些内容集中到 `RunConfigSO` 及其子配置资产里。策划可以通过替换或调整 ScriptableObject，快速得到不同版本的 Run 体验。

## 当前有效流程

当前肉鸽主循环使用 `GameFlowMode.RoundFlow`。

玩家从主菜单开始后，流程为：

```text
GameFlowController.StartRun(runConfig, RoundFlow)
  -> ApplyRunStartSettings
  -> InitializeRoundFlow
  -> RunRoundController.StartRun(runConfig, roundSettings)
  -> BuildNextOffer
  -> 玩家选择 Classic / Escort / Skip
  -> LevelPlayer.PlayLevel
  -> 战斗结算
  -> PostCombatOffer: Shop / Event
  -> AdvanceEncounterCycle
  -> 下一轮
```

一轮的结构可以理解为：

```text
State A: 主战斗选择
  Classic 经典推箱子
  Escort 护送关
  Skip 放弃本轮并拿补偿奖励

State B: 战后选择
  Shop 商店
  Event 随机事件
```

`RunRoundConfigSO.encounterCyclesPerRound` 决定多少次 State A -> State B 循环后推进到下一轮。

## 配置资产结构

当前默认资产位于：

```text
Assets/Settings/RunConfig/RunConfig.asset
Assets/Settings/RunConfig/Domains/
```

建议保持“总入口 + 领域配置”的组织方式：

```text
RunConfig.asset
  Domains/RunStart/RunStartSettings.asset
  Domains/RunRound/RunRoundConfig.asset
  Domains/RunDifficultyConfig.asset
  Domains/RunRewardConfig.asset
  Audio/BGM 配置
```

这样一局 Run 的整体规则可以通过一个 `RunConfigSO` 找到，具体数值又不会全部堆在同一个资产里。

## RunConfigSO

`RunConfigSO` 是外层总入口，字段职责如下：

```text
configId        配置 ID，用于区分不同 Run 版本
displayName     显示名，用于主菜单或调试显示
startSettings   开局资源配置
roundSettings   当前有效肉鸽循环配置
difficultySettings 难度成长配置
rewardSettings  关卡奖励配置
mainMenuBgm     主菜单 BGM
bgmPlaylist     Run 内 BGM 列表
```

当前不建议把大量细节直接塞进 `RunConfigSO`。它应该只做装配入口。

## RunStartSettings

`RunStartSettings` 控制一局 Run 的初始状态：

```text
startingDeck     初始卡组
startingGold     初始金币
startingMaxHp    初始最大生命值
startingHp       初始生命值
targetHandCount  目标手牌数
maxHandCount     最大手牌数
autoRefill       是否自动补手牌
```

运行时由 `GameFlowController.ApplyRunStartSettings()` 消费：

```text
CardDeckFacade.ReplaceWithStartingDeck
RunInventoryFacade.Reset
RunPlayerStatusFacade.Reset
```

策划意义：

```text
可以快速制作不同开局流派
可以测试高压开局、富裕开局、低血开局
可以通过初始卡组控制玩家第一局的学习曲线
```

## RunRoundConfigSO

`RunRoundConfigSO` 是当前肉鸽主循环的核心配置。

### Round Flow

```text
totalRounds              一局 Run 的总轮数
encounterCyclesPerRound  每轮包含多少次主战斗循环
seed                     随机种子，0 表示每次随机
```

`totalRounds` 控制整体长度，`encounterCyclesPerRound` 控制每轮密度。

例如：

```text
totalRounds = 12
encounterCyclesPerRound = 3
```

表示整局最多经历 12 轮，每轮包含 3 次主战斗选择与战后选择。

### State A - 主战斗选择

```text
levelSourceDatabase          经典关和护送生成可用的关卡来源库
classicFeatureSelectionTable 经典关筛选表
escortFeatureSelectionTable  护送关筛选表
escortGenerationConfig       护送关生成参数
skipRewardPool               跳过本轮的补偿奖励池
classicEscortAlternationGold Classic 和 Escort 交替完成时的额外金币
```

每次进入主战斗选择时，`RunRoundController.BuildNextOffer()` 会生成：

```text
ClassicLevel
EscortLevel
SkipReward
AlternateBonusGold
```

策划可以通过这里控制：

```text
经典关出现的难度段
护送关使用哪些地图特征
玩家跳过战斗时拿到什么补偿
是否鼓励玩家在 Classic 和 Escort 之间切换
```

### State B - 战后选择

```text
shopItemPool    商店商品池
eventStagePool  随机事件池
```

战斗成功后，`RunRoundController.BuildPostCombatOffer()` 会生成：

```text
Shop
EventPack
```

商店不是固定写死的场景，而是由 `shopItemPool` 生成运行时商店。

事件使用 `EventStagePoolSO` 抽取 NekoGraph 事件 Pack。这里的 NekoGraph 只负责“事件流程如何执行”，不是当前 RunConfig 的主配置入口。

## RunDifficultyConfigSO

`RunDifficultyConfigSO` 控制随进度变化的难度快照。

核心字段：

```text
overallDifficulty
enemySpawnDifficultyProfile
enemySpawnTimingProfile
enemyHealthMultiplierByProgress
enemyAttackMultiplierByProgress
rewardMultiplierByProgress
```

运行时会根据当前轮数计算进度：

```text
progress = 当前轮 / 总轮数
```

然后生成 `RunDifficultySnapshot`，传给 `LevelPlayer`：

```text
EnemyHealthMultiplier
EnemyAttackMultiplier
RewardMultiplier
EnemySpawnDifficultyProfile
EnemySpawnTimingProfile
```

策划意义：

```text
可以让敌人血量随后期增长
可以让敌人攻击随后期增长
可以让奖励随难度同步提高
可以调整刷怪强度和刷怪节奏
```

## RunRewardConfigSO

`RunRewardConfigSO` 控制关卡内奖励结算。

Classic 奖励：

```text
classicPhaseOneFirstBoxGold
classicPhaseOneBoxGoldStep
classicPhaseTwoFirstBoxGold
classicPhaseTwoBoxGoldStep
```

Escort 奖励：

```text
escortRewardBoxGold
escortCompletionGold
escortCompletionRewardPool
```

奖励会受到 `RunDifficultySnapshot.RewardMultiplier` 影响。

策划意义：

```text
Classic 可以按箱子数量给阶梯金币
Escort 可以按奖励箱和完成奖励给金币
后期可以通过 rewardMultiplierByProgress 放大奖励
```

## EventStagePoolSO

`EventStagePoolSO` 是战后随机事件池。

每个条目包含：

```text
stageId
stagePack
weight
enabled
```

`stagePack` 是一个 NekoGraph Pack 文本资源。运行时抽到后交给 `RunStageFacade.TryRunStagePack()` 执行。

这让事件可以独立配置为：

```text
给金币
给卡牌
扣血换奖励
商店外事件
带选择的事件流程
```

当前文档只把它视为 RunConfig 的事件内容池，不展开 NekoGraph 底层节点编辑细节。

## 当前废案：RunRouteConfigSO

`RunRouteConfigSO` 曾用于路线图模式 `GameFlowMode.RouteMap`。

它负责：

```text
路线层数
车道数
路线形状
节点类型池
Classic / Encounter / Shop / Escort 内容池
路线节点解锁
```

但当前肉鸽主循环已经转向 `RoundFlow + RunRoundConfigSO`。

因此上交说明中应明确：

```text
RunRouteConfigSO 是早期路线图方案
当前 demo 的肉鸽进程配置以 RunConfigSO + RunRoundConfigSO 为准
RouteMap 可作为后续扩展方向，不作为当前验收重点
```

这样可以避免评审误以为项目同时维护两套互相竞争的主流程。

## 策划配置工作流

### 新建一套 Run

```text
1. 创建 RunConfigSO
2. 创建或复制 RunStartSettings
3. 创建或复制 RunRoundConfigSO
4. 创建或复制 RunDifficultyConfigSO
5. 创建或复制 RunRewardConfigSO
6. 在 RunConfigSO 中引用这些子配置
7. 在 GameFlowController 或主菜单入口中指定该 RunConfigSO
```

### 调整开局体验

```text
修改 RunStartSettings
  startingDeck
  startingGold
  startingHp
  targetHandCount
```

适合测试：

```text
新手开局
高压开局
指定卡组流派
低资源挑战
```

### 调整一局 Run 的长度

```text
修改 RunRoundConfigSO
  totalRounds
  encounterCyclesPerRound
```

适合测试：

```text
短局 demo
标准局
长局压力测试
```

### 调整战斗选择

```text
修改 RunRoundConfigSO
  classicFeatureSelectionTable
  escortFeatureSelectionTable
  escortGenerationConfig
  skipRewardPool
  classicEscortAlternationGold
```

适合测试：

```text
Classic 和 Escort 的节奏比例
跳过战斗是否有足够吸引力
交替完成奖励是否能引导玩家换模式
```

### 调整战后内容

```text
修改 RunRoundConfigSO
  shopItemPool
  eventStagePool
```

适合测试：

```text
商店强度
事件出现内容
战后选择的收益差异
```

### 调整成长曲线

```text
修改 RunDifficultyConfigSO
  enemyHealthMultiplierByProgress
  enemyAttackMultiplierByProgress
  rewardMultiplierByProgress
  enemySpawnDifficultyProfile
  enemySpawnTimingProfile
```

适合测试：

```text
前期轻松、后期高压
全程稳定压力
奖励和难度同步增长
```

## 验收清单

基础验收：

```text
主菜单能读取当前 RunConfigSO
点击开始后进入 RoundFlow
开局卡组、金币、血量按 RunStartSettings 生效
每轮能生成 Classic / Escort / Skip 选择
选择 Classic 后能进入经典关
选择 Escort 后能进入护送关
Skip 能发放配置的奖励
战斗成功后进入结算
结算后出现 Shop / Event 选择
Shop 使用 RunRoundConfigSO.shopItemPool
Event 使用 RunRoundConfigSO.eventStagePool
完成 totalRounds 后进入 RunComplete
失败后进入 Defeat
```

数值验收：

```text
不同 routeLayer / RoundIndex 下 difficulty progress 正确变化
敌人血量倍率随 enemyHealthMultiplierByProgress 生效
敌人攻击倍率随 enemyAttackMultiplierByProgress 生效
奖励倍率随 rewardMultiplierByProgress 生效
Classic 奖励使用 RunRewardConfigSO
Escort 奖励使用 RunRewardConfigSO
```

配置验收：

```text
替换 RunConfigSO 后，开局和循环规则随之改变
替换 RunStartSettings 后，只影响开局资源
替换 RunRoundConfigSO 后，只影响肉鸽循环
替换 RunDifficultyConfigSO 后，只影响难度成长
替换 RunRewardConfigSO 后，只影响奖励结算
```

## 当前限制与后续方向

当前限制：

```text
RunConfig 资产之间缺少一键校验
RunRoundConfigSO 中字段较多，需要更清晰的 Inspector 分组说明
EventStagePoolSO 只负责抽事件，不负责展示事件内容预览
Shop 是运行时生成，调试时需要额外确认商品池输出
RunRouteConfigSO 仍保留在工程中，容易造成理解干扰
```

后续方向：

```text
增加 RunConfig 校验面板
显示一局 Run 的预估结构预览
显示每轮 Classic / Escort / Skip / Shop / Event 的配置摘要
给事件池增加预览和权重分析
给奖励池增加期望收益统计
把废案 RouteMap 明确移入 Archive 或 Experimental 分类
```

## 提交说明口径

如果作为技术策划 demo 展示，可以这样概括：

```text
我把肉鸽流程拆成可配置的 RunConfig 总入口。
策划不需要改代码，就能调整开局资源、Run 长度、战斗选择、战后商店/事件、难度成长和奖励曲线。
当前主流程使用 RoundFlow，RunRouteConfigSO 是早期路线图原型，已不作为当前 demo 的主配置方案。
```

