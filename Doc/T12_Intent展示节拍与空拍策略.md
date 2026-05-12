# Intent 展示节拍与空拍策略

日期：2026-05-13

## 命题

`IntentSystem` 运行时不是先拿到一个完整小节再决定是否压缩，而是从已提交的 intent 里拼出一串展示拍。设计讨论里常说“小节”和“空拍”，但程序实际处理的是“哪些 intent 应该进入哪些 beat slot”。

因此这套规则要同时说明两件事：

- 敌人 intent 如何被分配到玩家拍后面的敌人拍。
- 当某个敌人拍没有内容时，空拍是否仍然保留。

本文只描述展示/节奏语义，不改变 intent 的战斗结算含义。

## 符号

```text
P = Player intent
E = Enemy intent，敌人侧所有可合并 intent 的单一合并拍
M = Move(Enemy intent)，敌人移动拍
A = Attack.Etc(Enemy intent)，敌人非移动拍；当前包括 Attack、Spawn，以及可进入 AllInOne 合并拍的其他非 Move intent
_ = empty presentation beat，没有 intent，但保留一拍展示/音频节奏
```

## AllInOneBatch

`AllInOneBatch` 用在 2/4 和 4/4 旋律。玩家 intent 占第 1 拍，敌人侧所有可合并 intent 合并到第 2 拍。

完整结构：

```text
P E | P E | P E | P E
```

连续写法：

```text
PEPEPEPE
```

设计目的：2/4 和 4/4 的玩家-敌人往返感更强，所有敌人行为压成一个敌人拍，可以让回合节奏保持清晰，避免敌人数量或敌人 intent 类型把节奏拉散。

## AllInTwoBatch

`AllInTwoBatch` 用在 3/4 和 6/8 旋律。玩家 intent 占第 1 拍，敌人 Move 占第 2 拍，敌人非 Move intent 占第 3 拍。

完整结构：

```text
P M A | P M A
```

连续写法：

```text
PMAPMA
```

设计目的：3/4 和 6/8 不适合继续使用 PE 的二拍往返结构。把敌人侧拆成 M 和 A，可以让敌人移动和攻击/刷怪落在小节内稳定位置，避免三拍或复合六拍音乐被玩家一拍、敌人一拍的结构顶爆。

## 空拍边界

边界问题是：当某一类敌人 intent 不存在时，是否保留对应的 beat slot。

当前策略分两层：

1. 只要本轮存在任意敌人内容，就保留当前展示模式的完整敌人拍结构。
2. 只有本轮完全没有敌人内容，也就是玩家单意图轮，才允许由 `completeSingleBeatMeasure` 决定是否补齐空拍。

`completeSingleBeatMeasure = false` 时，玩家单意图轮会塌缩为单拍：

```text
AllInOneBatch: P_  -> P
AllInTwoBatch: P__ -> P
```

`completeSingleBeatMeasure = true` 时，玩家单意图轮也补齐当前展示模式的小节：

```text
AllInOneBatch: P_
AllInTwoBatch: P__
```

注意：这个字段只处理“完全没有敌人内容”的边界。只要有任意敌人内容，缺失的敌人拍仍然保留为空拍。

```text
AllInOneBatch:
E 存在:     PE
E 不存在:   P 或 P_，由 completeSingleBeatMeasure 决定

AllInTwoBatch:
M 和 A 都存在: PMA
只有 M:       PM_
只有 A:       P_A
M/A 都不存在: P 或 P__，由 completeSingleBeatMeasure 决定
```

## 字段语义

### enemyIntentPresentationMode

选择敌人 intent 如何进入玩家拍后面的展示拍。

```text
AllInOneBatch = P + E
AllInTwoBatch = P + M + A
```

`BgmRecordPlayer` 会根据当前 `BgmPromptSO.BeatGrouping` 自动路由：

```text
DupleBeat / QuadBeat     -> AllInOneBatch
TripleBeat / CompoundSix -> AllInTwoBatch
```

### completeSingleBeatMeasure

控制玩家单意图轮是否补齐当前展示模式的空敌人拍。

这个名字从运行视角看会稍微别扭：设计讨论常说“是否压缩小节”，但程序是在拼拍子。实际语义不是修改已有小节，而是在构建 execution steps 时决定：

```text
没有敌人内容时，是否仍然插入 empty enemy presentation beat。
```

它不影响这些情况：

- 不改变玩家 intent、敌人 intent 的内容。
- 不改变战斗结算顺序。
- 不会把 `PM_` 压成 `PM`。
- 不会把 `P_A` 压成 `PA`。

它只影响这些情况：

```text
P_  是否塌缩成 P
P__ 是否塌缩成 P
```

## 维护约定

- 新增 BGM 类型时，先确定它应该使用 PE 还是 PMA，再更新 `BgmPromptSO.UsesAllInTwoPresentation`。
- 新增敌人 intent 类型时，必须判断它属于 M，还是属于 A/E。
- 如果新增的 intent 不能安全批处理，不要直接塞进 AllInOne/AllInTwo；先确认 `BeforeIntentExecute`、`AfterIntentExecute`、`ResolveWorldState` 和展示等待语义是否仍然成立。
- 调试节奏错位时，先看 `BgmRecordPlayer` 的路由日志，再看 `IntentSystem` 的 execution steps 日志。
