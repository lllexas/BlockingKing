# 肉鸽难度曲线与 Round 配置设计原理说明

日期：2026-05-13

这份文档不讲“怎么点 Inspector”。

这份文档只讲三件事：

1. 这套 SO 配置体系想解决什么问题
2. 为什么要按现在这个层级拆分
3. 以后继续加内容时，应该往哪一层扩

## 1. 总目标

这套肉鸽配置体系的目标，不是单纯把一堆数值挪到 SO 里。

它真正想解决的是：

- 让一局 run 的长度可控
- 让每一段战斗压力可以分阶段组织
- 让地图形态、敌人构成、刷怪节奏、奖励节奏分别独立调节
- 让策划能在不改代码的前提下，持续迭代难度曲线

换句话说，这套系统想提供的是：

一个可以长期运营、长期迭代、并且能被策划直接操作的肉鸽调参框架。

## 2. 总体分层思路

当前这套配置不是按“代码模块”拆的，而是按“策划真正会遇到的设计问题”拆的。

从上到下，可以理解成六层：

1. 总装配层
2. 局流程层
3. 关卡段落层
4. 战斗压力层
5. 经济奖励层
6. 内容池层

每一层只回答一类问题。

这样做的核心目的，是让每个 SO 都有明确职责，不让多个维度混在一起。

## 3. 总装配层

对应资产：

- `RunConfigSO`
- 当前主资产：`Assets/Settings/RunConfig/RunConfig.asset`

这一层的职责不是调参，而是“装配”。

它负责把一个 run 会用到的各个域配置接起来：

- 开局配置
- 路线配置
- round 配置
- 难度配置
- 奖励配置
- BGM 配置

这一层的设计原则是：

- 自己尽量少表达玩法规则
- 主要承担“引用关系入口”

因此 `RunConfig` 更像一张总线路图，而不是具体数值表。

## 4. 局流程层

对应资产：

- `RunRoundConfigSO`
- 当前主资产：`Assets/Settings/RunConfig/Domains/RunRoundConfig.asset`

这一层回答的问题是：

- 一局总共有多少 Round
- 每个 Round 有多少次主战斗循环
- 主战斗之后接什么内容
- Classic 和 Escort 在这一局里如何交替出现

这层决定的是“run 的骨架”。

也就是说，它不负责决定“怪有多强”，而是负责决定“玩家一局会经历多少次内容切换与决策”。

它管的是节奏厚度，不是战斗细节。

在设计上，这层被单独拿出来，是因为：

- 一局长度
- 单轮厚度
- 战后内容频率

这些东西本质上属于“局结构设计”，应该和数值难度分开。

## 5. 关卡段落层

对应资产：

- `LevelFeatureSelectionTableSO`
- `LevelFeatureFilterSO`

当前主资产：

- `Assets/Settings/RunConfig/Domains/RunRound/ClassicLevelFeatureSelectionTable.asset`
- `Assets/Settings/RunConfig/Domains/RunRound/EscortLevelFeatureSelectionTable.asset`
- `Assets/Settings/LevelFeatureFilters/*.asset`

这一层回答的问题是：

- 前期、中期、后期分别抽什么样的图
- 哪些地图适合当前轮次
- 地图尺寸、墙密度、箱子压力如何分段变化

这里故意拆成两层：

- `SelectionTable` 管“第几轮该用哪套规则”
- `Filter` 管“这套规则本身长什么样”

这样拆的意义是：

- 策划可以复用同一套地图规则到多个轮次
- 也可以只换轮次分段，而不重写规则内容

这层本质上是在组织“关卡段落感”。

它控制的不是敌人，而是战场。

## 6. 战斗压力层

这一层又分成三个子层：

- 数值曲线
- 敌人构成
- 刷怪节奏

### 6.1 数值曲线

对应资产：

- `RunDifficultyConfigSO`
- 当前主资产：`Assets/Settings/RunConfig/Domains/RunDifficultyConfig.asset`

这一层回答的问题是：

- 随着 run 推进，敌人的血量、攻击、奖励倍率如何变化

它的职责是提供“连续变化的倍率背景”。

也就是说，它负责描述：

- 前期偏平
- 中期开始抬高
- 后期进入更高压区

这是全局性的、随进度变化的难度底盘。

### 6.2 敌人构成

对应资产：

- `EnemySpawnDifficultyProfileSO`
- 当前主资产：`Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnDifficultyProfile.Default.asset`

这一层回答的问题是：

- 当前阶段更倾向刷哪些敌人
- 新敌人从哪一段开始进入战局
- 不同敌人在同一阶段的占比如何

这一层不改地图，也不改数值曲线。

它表达的是敌人构成，也就是“压力来自谁”。

### 6.3 刷怪节奏

对应资产：

- `EnemySpawnTimingProfileSO`
- 当前主资产：`Assets/Settings/RunConfig/Domains/RunDifficulties/EnemySpawnTimingProfile.asset`

这一层回答的问题是：

- 多久刷一次
- 开局多久开始刷
- 不同刷怪点是否需要错峰

这一层表达的是“压力来得有多快”，不是“压力本身是谁”。

所以它被单独拆出，而不是塞进敌人构成表里。

## 7. 经济奖励层

对应资产：

- `RunRewardConfigSO`
- 当前主资产：`Assets/Settings/RunConfig/Domains/RunRewardConfig.asset`

这一层回答的问题是：

- 经典关给多少金
- 押送关给多少金
- 分阶段箱子奖励怎么计算
- 完成关卡后的基础回报如何组织

这一层不参与地图抽取，也不参与敌人生成。

它专门负责：

- 玩家打完之后拿到什么
- 经济节奏怎么走

这层单独存在，是为了让“战斗压力”与“经济回报”能够分别调整。

## 8. 内容池层

对应资产：

- `EventStagePoolSO`
- `ShopItemPoolSO`
- 部分 `StagePoolSO`

这一层回答的问题是：

- 战后能遇到哪些商店内容
- 战后能遇到哪些事件内容
- 某一类节点从什么内容池里抽

这一层的核心不是难度倍率，而是内容分发。

它负责让一局 run 在结构确定之后，真正落到具体内容上。

## 9. 当前层级关系的实际意义

如果从设计工作流来理解，这几层的关系是：

1. `RunRoundConfig`
   - 先决定一局怎么走
2. `SelectionTable + Filter`
   - 再决定每一段战场长什么样
3. `RunDifficultyConfig`
   - 再决定数值底盘怎么抬
4. `EnemySpawnDifficultyProfile`
   - 再决定敌人构成怎么换
5. `EnemySpawnTimingProfile`
   - 再决定压力推进速度
6. `RunRewardConfig`
   - 最后决定玩家拿多少回报

这就是这套配置体系真正的组织顺序。

它不是代码调用顺序，而是设计控制顺序。

## 10. 扩展原则

以后继续加内容时，优先按下面的边界扩。

### 10.1 想加新的局结构规则

去：

- `RunRoundConfigSO`

适合放这里的内容：

- 新的 Round 长度规则
- 新的主战斗循环厚度
- 新的战后流程分支

### 10.2 想加新的关卡段落

去：

- `LevelFeatureSelectionTableSO`
- `LevelFeatureFilterSO`

适合放这里的内容：

- 新的前中后期地图段
- 新的地图尺寸 / 墙率 / 箱子压力分段

### 10.3 想加新的全局难度走势

去：

- `RunDifficultyConfigSO`

适合放这里的内容：

- 新的血量曲线
- 新的攻击曲线
- 新的奖励倍率曲线

### 10.4 想加新的敌人阶段

去：

- `EnemySpawnDifficultyProfileSO`

适合放这里的内容：

- 新敌人何时加入
- 不同轮次的敌人权重切换

### 10.5 想加新的刷怪节奏段

去：

- `EnemySpawnTimingProfileSO`

适合放这里的内容：

- 新的 interval / delay / jitter 分段

### 10.6 想加新的经济模型

去：

- `RunRewardConfigSO`

适合放这里的内容：

- 新的奖励公式
- 新的模式奖励口径
- 新的结算回报口径

### 10.7 想加新的商店 / 事件内容

去：

- `ShopItemPoolSO`
- `EventStagePoolSO`

适合放这里的内容：

- 新事件条目
- 新商店条目
- 新的内容池配比

## 11. 当前体系的设计结论

这套 SO 体系的本质，不是“把配置拆碎”。

它的本质是：

- 用流程层控制一局的骨架
- 用关卡段落层控制战场形态
- 用战斗压力层控制敌人威胁
- 用经济层控制回报节奏
- 用内容池层控制具体内容分发

因此以后做扩展时，应该优先保持这个边界。

不要让一个 SO 同时承担：

- 流程
- 地图
- 数值
- 刷怪
- 奖励

中的多个职责。

边界一旦保持清楚，策划调参和后续维护都会稳定很多。
