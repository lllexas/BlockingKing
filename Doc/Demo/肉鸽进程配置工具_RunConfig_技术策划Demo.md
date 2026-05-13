# 肉鸽进程配置工具 RunConfig - 技术策划 Demo

## 一句话定位

RunConfig 是当前项目的肉鸽流程配置入口：它把一局 Run 的开局资源、主循环、战后选择、难度成长、奖励曲线和音乐配置拆成可组合的 ScriptableObject。

当前有效主流程是：

```text
RunConfigSO + RunRoundConfigSO -> GameFlowMode.RoundFlow
```

`RunRouteConfigSO` 是早期路线图方案，目前按废案处理，不作为本 demo 的主流程。

## 解决的问题

肉鸽游戏的流程迭代频率很高。策划经常需要调整：

```text
开局卡组和资源
一局有多少轮
每轮给玩家什么选择
战斗后是否进商店或事件
商店商品池
随机事件池
敌人强度曲线
奖励收益曲线
Classic / Escort 两种关卡模式的节奏
```

如果这些逻辑写死在代码里，每次调节都需要程序介入。

RunConfig 的目标是把这些内容拆成配置资产，让策划可以通过 Inspector 调整一局 Run 的结构和节奏。

## 当前主流程

当前肉鸽使用 `RoundFlow`，由 `GameFlowController` 启动，`RunRoundController` 执行。

```text
主菜单开始
  -> GameFlowController.StartRun(runConfig, RoundFlow)
  -> 应用 RunStartSettings
  -> RunRoundController.StartRun
  -> 生成本轮主战斗选择
  -> 玩家选择 Classic / Escort / Skip
  -> 战斗或跳过
  -> 战斗结算
  -> 生成战后 Shop / Event 选择
  -> 进入下一次 encounter cycle
  -> 达到总轮数后 RunComplete
```

一轮可以理解成两个阶段：

```text
State A: 主战斗选择
  Classic 经典推箱子
  Escort 护送关
  Skip 跳过并获得补偿

State B: 战后选择
  Shop 商店
  Event 随机事件
```

## 配置资产结构

当前默认配置位于：

```text
Assets/Settings/RunConfig/RunConfig.asset
Assets/Settings/RunConfig/Domains/
```

配置分层：

```text
RunConfigSO
  RunStartSettings
  RunRoundConfigSO
  RunDifficultyConfigSO
  RunRewardConfigSO
  mainMenuBgm
  bgmPlaylist
```

`RunConfigSO` 本身只做装配入口，具体规则拆到各个领域配置中。

## RunConfigSO：总装配入口

```text
configId              配置 ID
displayName           显示名
startSettings         开局资源
roundSettings         肉鸽主循环
difficultySettings    难度成长
rewardSettings        奖励结算
mainMenuBgm           主菜单音乐
bgmPlaylist           Run 内音乐列表
```

策划可以复制一份 `RunConfigSO`，替换其中部分子配置，快速得到不同版本的 Run。

例如：

```text
新手版 RunConfig
高压版 RunConfig
短局测试 RunConfig
高奖励爽局 RunConfig
```

## RunStartSettings：开局配置

控制玩家进入 Run 时的初始状态：

```text
startingDeck
startingGold
startingMaxHp
startingHp
targetHandCount
maxHandCount
autoRefill
```

策划用途：

```text
调整初始卡组
测试低血开局
测试高金币开局
控制第一轮手牌体验
```

运行时由 `GameFlowController.ApplyRunStartSettings()` 写入：

```text
CardDeckFacade
RunInventoryFacade
RunPlayerStatusFacade
```

## RunRoundConfigSO：肉鸽循环核心

这是当前 demo 最重要的流程配置。

### 一局长度

```text
totalRounds
encounterCyclesPerRound
seed
```

`totalRounds` 控制整局长度。  
`encounterCyclesPerRound` 控制每轮包含多少次主战斗循环。  
`seed` 用于固定随机序列，方便调试。

### 主战斗选择

```text
levelSourceDatabase
classicFeatureSelectionTable
escortFeatureSelectionTable
escortGenerationConfig
skipRewardPool
classicEscortAlternationGold
```

每次进入 State A 时，系统会生成：

```text
一个 Classic 关卡
一个 Escort 关卡
一个 Skip 奖励
Classic / Escort 交替奖励提示
```

策划可以用它控制：

```text
Classic 出现什么类型的经典关
Escort 使用什么地图生成约束
跳过战斗是否值得
是否鼓励玩家交替体验两种关卡模式
```

### 战后选择

```text
shopItemPool
eventStagePool
```

战斗成功后，系统生成：

```text
运行时商店
随机事件 Pack
```

商店来自 `ShopItemPoolSO`。  
事件来自 `EventStagePoolSO`，事件内部流程由 NekoGraph Pack 执行。

这里的 NekoGraph 是事件执行层，不是当前 RunConfig 的主配置入口。

## RunDifficultyConfigSO：难度成长

控制一局 Run 中敌人与奖励随进度变化的曲线：

```text
overallDifficulty
enemySpawnDifficultyProfile
enemySpawnTimingProfile
enemyHealthMultiplierByProgress
enemyAttackMultiplierByProgress
rewardMultiplierByProgress
```

运行时会根据当前轮数计算 progress，然后生成 `RunDifficultySnapshot` 传给 `LevelPlayer`。

策划用途：

```text
前期放松，后期加压
提高后期敌人血量
提高后期敌人攻击
调整刷怪节奏
让奖励随难度同步增长
```

## RunRewardConfigSO：奖励结算

控制 Classic 和 Escort 的基础奖励：

```text
Classic:
  classicPhaseOneFirstBoxGold
  classicPhaseOneBoxGoldStep
  classicPhaseTwoFirstBoxGold
  classicPhaseTwoBoxGoldStep

Escort:
  escortRewardBoxGold
  escortCompletionGold
  escortCompletionRewardPool
```

奖励会受到 `RunDifficultySnapshot.RewardMultiplier` 影响。

策划用途：

```text
控制经典关箱子收益
控制护送关完成收益
控制后期收益膨胀速度
让高风险关卡有更高回报
```

## EventStagePoolSO：事件池

战后事件从 `EventStagePoolSO` 抽取。

每个条目包含：

```text
stageId
stagePack
weight
enabled
```

`stagePack` 是一个 NekoGraph Pack，可以表达带选择、等待、奖励、扣血等事件流程。

对策划来说，它的意义是：战后事件不需要写死在 `RunRoundController` 里，可以作为内容池独立扩展。

## RunRouteConfigSO 的处理

`RunRouteConfigSO` 曾用于 `GameFlowMode.RouteMap`，即路线图式肉鸽原型。

它包含：

```text
路线层数
车道数
路线形状
节点类型池
Classic / Encounter / Shop / Escort 内容池
路线节点解锁
```

但当前 demo 的正式主流程使用 `RoundFlow`。


## 策划工作流示例

### 做一个短局测试版本

```text
1. 复制 RunConfigSO
2. 复制 RunRoundConfigSO
3. totalRounds 改为 3
4. encounterCyclesPerRound 改为 1
5. seed 固定为一个非 0 值
6. 在新 RunConfigSO 中引用新 RunRoundConfigSO
7. 从主菜单启动该 RunConfig
```

### 做一个高压后期版本

```text
1. 复制 RunDifficultyConfigSO
2. 提高 enemyHealthMultiplierByProgress 后段曲线
3. 提高 enemyAttackMultiplierByProgress 后段曲线
4. 调整 enemySpawnDifficultyProfile
5. 在 RunConfigSO 中替换 difficultySettings
```

### 做一个高奖励版本

```text
1. 复制 RunRewardConfigSO
2. 提高 Escort 完成金币
3. 提高 Classic 箱子金币阶梯
4. 在 RunDifficultyConfigSO 中提高 rewardMultiplierByProgress 后段
5. 在 RunConfigSO 中引用新的奖励和难度配置
```

### 增加战后事件内容

```text
1. 创建或准备一个事件 stagePack
2. 加入 EventStagePoolSO.entries
3. 配置 stageId、weight、enabled
4. 运行 RoundFlow
5. 战斗后选择 Event，验证事件是否能被抽到并执行
```

## 对技术策划能力的体现

这部分 demo 体现的是“流程配置能力”，不是单个功能点。

```text
把一局 Run 拆成开局、循环、难度、奖励、内容池等领域
让不同领域可以独立替换和复用
把高频调参内容交给 ScriptableObject
保留随机种子以支持复现和调试
用配置资产组织 Classic / Escort / Shop / Event 的内容来源
把 NekoGraph 用在事件流程执行层，而不是让它吞掉整个 RunConfig 入口
明确废案边界，避免路线图原型干扰当前主流程
```

## 当前完成度

已经完成：

```text
RunConfigSO 总入口
RunStartSettings 开局配置
RunRoundConfigSO 主循环
Classic / Escort / Skip 三选一
战斗结算
Shop / Event 战后选择
RunDifficultyConfigSO 难度快照
RunRewardConfigSO 奖励结算
EventStagePoolSO 事件池
主菜单 BGM 和 Run 内 BGM 分离
```

仍在迭代：

```text
RunConfig 一键校验
Inspector 上的配置摘要
事件池预览
奖励期望收益统计
商店输出预览
废案 RouteMap 的归档整理
```

## 相关文档

- [肉鸽进程配置工具 RunConfig 说明](../T16_肉鸽进程配置工具_RunConfig说明.md)
- [卡牌前端维护文档](../T04_卡牌前端维护文档.md)
- [LevelPlayer 维护文档](../T05_LevelPlayer维护文档.md)
- [音频总线与 Beat 音效维护](../T10_音频总线与Beat音效维护.md)

