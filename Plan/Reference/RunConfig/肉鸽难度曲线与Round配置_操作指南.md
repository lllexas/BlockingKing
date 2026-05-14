# 肉鸽难度曲线与 Round 配置操作指南

日期：2026-05-13

这份文档只回答一件事：
策划想达成某个目标时，应该改哪个 SO，改完会影响什么。

本文按“看了就会配”的标准写，不展开设计原理。

## 1. 总入口

总配置入口：

- `Assets/Settings/RunConfig/RunConfig.asset`

这个资产是总开关。它把 run 相关的几个域配置串起来：

- `startSettings`：开局手牌、初始状态
- `routeSettings`：大地图层数、路线形状、节点类型池
- `roundSettings`：每轮主战斗/战后选择的流程
- `difficultySettings`：敌人数值曲线、刷怪构成、刷怪节奏
- `rewardSettings`：经典 / 押送奖励

如果只是调单一模块，不要先动 `RunConfig.asset` 本体；优先点进去改它引用的子 SO。

## 2. 先记住：哪些 SO 给策划改，哪些最好别乱碰

日常策划直接改这些：

- `Assets/Settings/RunConfig/Domains/RunRoundConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficultyConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunRewardConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/ClassicLevelFeatureSelectionTable.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/EscortLevelFeatureSelectionTable.asset`
- `Assets/Settings/LevelFeatureFilters/*.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnDifficultyProfile.Default.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnTimingProfile.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/EventStagePool.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/ShopItemPool.asset`

尽量不要作为日常调参入口的：

- `RunConfig.asset`
- `RunRouteConfigSO` 对应资产
- `StagePoolSO`
- `StageTypePoolSO`
- `LevelCollageGenerationSettings.asset`

原因很简单：这些更偏系统层或旧兼容层，乱改更容易把别的流程一起带坏。

## 3. 常见目标，对应改哪里

### 3.1 我要让一局变长 / 变短

改：

- `Assets/Settings/RunConfig/Domains/RunRoundConfig.asset`

看这两个字段：

- `totalRounds`
- `encounterCyclesPerRound`

含义：

- `totalRounds`：整局一共多少 Round
- `encounterCyclesPerRound`：每个 Round 内要完成多少次 “主战斗 -> 战后选择” 循环

直接效果：

- 增大 `totalRounds`：整局更长
- 增大 `encounterCyclesPerRound`：单个 Round 更厚、更拖时长

推荐：

- 想做“完整跑一局更久”，优先调 `totalRounds`
- 想做“每个 Round 内内容更密”，再调 `encounterCyclesPerRound`

### 3.2 我要调前期 / 中期 / 后期关卡尺寸和箱子压力

改：

- `Assets/Settings/RunConfig/Domains/RunRound/ClassicLevelFeatureSelectionTable.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/EscortLevelFeatureSelectionTable.asset`
- `Assets/Settings/LevelFeatureFilters/*.asset`

核心逻辑：

- `LevelFeatureSelectionTable` 决定“第几轮该使用哪套筛选规则”
- `LevelFeatureFilter` 决定“这套规则允许什么样的地图被抽中”

`LevelFeatureSelectionTable` 里重点看：

- `rows`
- 每个 `row.roundIndex`
- 每个 `row.filter`
- `fallbackFilter`
- `boxCountMode`

`LevelFeatureFilter` 里重点看：

- `widthRange`
- `heightRange`
- `areaRange`
- `wallRateRange`
- `effectiveBoxRange`

直接理解：

- 想让前期地图更小：调前期 row 对应 filter 的 `width / height / area`
- 想让中后期箱子压力更大：调 `effectiveBoxRange`
- 想让地图更堵 / 更空：调 `wallRateRange`

注意：

- `row.roundIndex` 不是“只在这一轮生效”，而是“从这一轮开始，直到被后面的 row 接管”
- `boxCountMode`
  - `EffectiveBoxes`：更偏实际有效箱子压力，推荐继续用这个
  - `TotalBoxes`：按总箱子数筛

推荐工作流：

1. 先在 `SelectionTable` 里确定分段
2. 再去改每段挂的 `Filter`
3. 不要先乱加很多 row，先保证 4 到 6 段够用

### 3.3 我要调敌人的整体数值曲线

改：

- `Assets/Settings/RunConfig/Domains/RunDifficultyConfig.asset`

重点字段：

- `overallDifficulty`
- `enemyHealthMultiplierByProgress`
- `enemyAttackMultiplierByProgress`
- `rewardMultiplierByProgress`

直接理解：

- `overallDifficulty`：全局总倍率底盘
- `enemyHealthMultiplierByProgress`：敌人血量曲线
- `enemyAttackMultiplierByProgress`：敌人攻击曲线
- `rewardMultiplierByProgress`：奖励曲线

这里的 `progress` 是按路线进度算的，不是按当前战斗时长算的。

推荐：

- 想让前期舒服、后期陡一点：把曲线前半段压低，后半段抬高
- 想全程都难一点：先轻调 `overallDifficulty`
- 不要同时大幅拉高血量和攻击，体验容易直接发硬

### 3.4 我要调“刷什么怪”

改：

- `Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnDifficultyProfile.Default.asset`

这个表决定：

- 某个阶段开始，刷怪点更倾向刷哪些敌人 BP

重点看：

- `rows`
- 每个 `row.roundIndex`
- 每行里的 `enemies`
- 每个敌人的 `weight`

直接理解：

- 从某个 `roundIndex` 开始，当前行会成为可刷怪表
- 行内 `weight` 越高，该敌人越容易被选中

推荐：

- 新敌人先低权重混入，不要一上来主导整行
- 难度升级优先通过“加入新敌人 + 调权重”做，不要只靠堆数值

### 3.5 我要调“刷怪频率”

改：

- `Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnTimingProfile.asset`

重点字段：

- `spawnInterval`
- `initialDelay`
- `jitter`

直接理解：

- `spawnInterval`：两次成功刷怪之间隔多少 tick
- `initialDelay`：关卡开始后多久开始第一次刷
- `jitter`：给每个刷怪点加一点稳定随机偏移，避免刷怪完全同拍

调参建议：

- 想整体更急：减小 `spawnInterval`
- 想开局喘口气：增大 `initialDelay`
- 想少一点“所有点同时刷”的死板感：加少量 `jitter`

注意：

- 这个配置会覆盖掉部分 BP 自带的刷怪节奏字段
- 所以要调刷怪快慢，优先改这里，不要先怀疑地图 BP

### 3.6 我要调经典关 / 押送关奖励

改：

- `Assets/Settings/RunConfig/Domains/RunRewardConfig.asset`

经典关重点字段：

- `classicPhaseOneFirstBoxGold`
- `classicPhaseOneBoxGoldStep`
- `classicPhaseTwoFirstBoxGold`
- `classicPhaseTwoBoxGoldStep`

押送关重点字段：

- `escortRewardBoxGold`
- `escortCompletionGold`

直接理解：

- Classic 的两段奖励都是等差数列累加
- Escort 现在是：
  - 每推进一个箱子到目标点，给 `escortRewardBoxGold`
  - 完成关卡再给 `escortCompletionGold`

如果体感是：

- 玩家太穷：先加 `escortRewardBoxGold` 或 Classic 前段奖励
- 玩家中后期爆钱：优先压后段 step，不要只压首箱奖励

### 3.7 我要调经典 / 押送交替游玩的奖励倾向

改：

- `Assets/Settings/RunConfig/Domains/RunRoundConfig.asset`

重点字段：

- `classicEscortAlternationGold`

直接理解：

- 如果玩家这次完成的主战斗模式，和上一次完成的不同，就给这笔奖励
- 这是鼓励玩家不要只打一种模式

### 3.8 我要调战后商店 / 不期而遇内容

改：

- `Assets/Settings/RunConfig/Domains/RunRound/ShopItemPool.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/EventStagePool.asset`

直接理解：

- `ShopItemPool`：商店候选内容池
- `EventStagePool`：不期而遇内容池

这两个属于战后内容池，不是战斗难度本体。

## 4. 你如果只想调“难度曲线”，推荐只碰这 4 个资产

最常用的四个：

- `RunRoundConfig.asset`
- `RunDifficultyConfig.asset`
- `EnemySpawnDifficultyProfile.Default.asset`
- `EnemySpawnTimingProfile.asset`

对应关系：

- `RunRoundConfig`：一局多长、每轮多厚
- `RunDifficultyConfig`：敌人数值曲线
- `EnemySpawnDifficultyProfile`：刷怪构成
- `EnemySpawnTimingProfile`：刷怪节奏

如果只调这四个，基本不会把地图筛选体系改乱。

## 5. 推荐调参顺序

如果要做一版新的难度曲线，推荐顺序：

1. 先定 `RunRoundConfig`
2. 再定 `RunDifficultyConfig`
3. 再定 `EnemySpawnDifficultyProfile`
4. 最后定 `EnemySpawnTimingProfile`
5. 奖励不舒服时，再补 `RunRewardConfig`

不要反过来。

原因：

- 局长和轮次密度不定，后面的数值都容易白调

## 6. 一套稳妥的改法

如果你只想“稍微变难一点”，建议：

1. `RunDifficultyConfig.asset`
   - 轻微抬高 `enemyHealthMultiplierByProgress`
   - 攻击曲线先别大动
2. `EnemySpawnDifficultyProfile.Default.asset`
   - 中后期行里提高高级敌人权重
3. `EnemySpawnTimingProfile.asset`
   - 中后期轻微降低 `spawnInterval`

这样改的风险最小。

## 7. 排错时先看什么

如果结果不符合预期，按这个顺序查：

1. `RunConfig.asset` 有没有挂对目标子 SO
2. `RunRoundConfig.asset` 当前引用的是不是你以为的表
3. `SelectionTable` 的 `row.roundIndex` 有没有断层或覆盖错误
4. `Filter` 有没有把候选关卡全筛没
5. `EnemySpawnDifficultyProfile` 当前轮有没有对应 row
6. `EnemySpawnTimingProfile` 当前轮有没有对应 row

## 8. 当前项目中的推荐资产入口

建议策划从这些现成资产开始，不要自己先新建一套：

- `Assets/Settings/RunConfig/RunConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunRoundConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficultyConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunRewardConfig.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/ClassicLevelFeatureSelectionTable.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/EscortLevelFeatureSelectionTable.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnDifficultyProfile.Default.asset`
- `Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnTimingProfile.asset`

## 9. 一句话总结

策划如果只想把肉鸽调顺：

- 用 `RunRoundConfig` 定流程厚度
- 用 `SelectionTable + Filter` 定关卡段落
- 用 `RunDifficultyConfig` 定数值曲线
- 用 `EnemySpawnDifficultyProfile + TimingProfile` 定敌人压力
- 用 `RunRewardConfig` 收尾调经济
