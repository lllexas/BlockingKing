# 不期而遇 RunStage 制作指南

## 目标

不期而遇是一种独立的 RunStage。

一个不期而遇关卡用一个 `.nekograph` 表示。它通过 NekoGraph 的信号流组织事件文本、玩家选择、奖励发放和结束节点。

## 最小结构

```text
Root
  -> vfs.msg
       -> vfs.reward
       -> vfs.reward
       -> Destroy / Leaf
```

推荐最小链路：

```text
Root
  -> .msg: 遭遇文本
       choice 0 -> .reward: 奖励 A
       choice 1 -> .reward: 奖励 B
       choice 2 -> Destroy / Leaf
```

## 资源准备

### 1. 创建 RunMsgSO

菜单：

```text
Create -> BlockingKing -> Run Stage -> Message
```

字段：

```text
messageId   消息标识
title       标题
speaker     说话者
body        正文
choices[]   玩家可选项
```

`choices[]` 的顺序必须和 `.msg` 节点后续分支顺序一致。

### 2. 创建 RewardSO

菜单：

```text
Create -> BlockingKing -> Run Stage -> Reward
```

当前支持：

```text
RewardKind.AddCardsToDeck
```

配置：

```text
cards[]
  card   CardSO 模板
  count  加入牌库的数量
```

执行时会把 `CardSO` 模板复制成独立卡牌实例，并以内联 JSON 写入玩家牌库 Pack。

## NekoGraph 节点配置

### 1. 创建 .nekograph

新建一个 Pack，作为该不期而遇事件的完整流程。

Pack 里至少需要：

```text
Root
vfs.msg
一个或多个 vfs.reward
Destroy / Leaf
```

### 2. 配置 vfs.msg

VFS 节点：

```text
Extension: .msg
ContentKind: ScriptableObject
ContentSource: 引用资源
引用 RunMsgSO
```

执行行为：

```text
RunMsgResource.Execute
  -> PostSystem.Send("RunStage.Msg.Execute", RunMsgPayload)
  -> HandleResult.Wait
```

Payload 包含：

```text
Message       RunMsgSO
PackID        当前 Pack
SourceNodeId  当前 .msg 节点
SignalId      当前信号
Targets       当前节点的所有后续目标
```

前端收到 `"RunStage.Msg.Execute"` 后，显示文本和选项。

玩家选择第 `i` 个选项后，应取：

```text
payload.Targets[i].TargetNodeId
```

然后调用：

```csharp
GraphHub.Instance.DefaultRunner.ResumeSuspendedSignalToTarget(
    payload.PackID,
    payload.SignalId,
    payload.SourceNodeId,
    payload.Targets[i].TargetNodeId);
```

信号会继续流向对应分支。

### 3. 配置 vfs.reward

VFS 节点：

```text
Extension: .reward
ContentKind: ScriptableObject
ContentSource: 引用资源
引用 RewardSO
```

执行行为：

```text
RewardResource.Execute
  -> CardDeckFacade.AddCard(...)
  -> HandleResult.Push
```

也就是说，奖励发放后信号自动继续流向下一个节点。

### 4. 配置结束节点

奖励之后接 Destroy / Leaf / 空结束节点均可。

当前重点是让信号明确终止，不要回流到 `.msg` 或形成循环。

## 关键约束

1. `.msg` 是流程控制节点，不是纯展示数据。
2. `.msg` 必须走 Execute，因为它需要当前 `SignalContext`。
3. `.reward` 是独立后继节点，不要塞进 `RunMsgSO`。
4. `RunMsgSO.choices` 只负责显示选项文本，不负责保存奖励内容。
5. `.msg` 的分支目标来自 NekoGraph 连线或 `ChildNodeIDs`。
6. 玩家牌库不存 `CardSO` 引用，存独立卡牌实例的 inline JSON。

## 当前缺口

前端 UI 还未实现。

目前后端已经能：

```text
.msg Execute -> 发 RunStage.Msg.Execute -> Wait
玩家选择后 Resume -> .reward Execute -> 写入牌库 -> Push
```

后续需要补：

```text
RunStageMsgPanel
  订阅 RunStage.Msg.Execute
  显示 RunMsgSO
  按钮选择分支
  调 ResumeSuspendedSignalToTarget
```

## 待优化记录

### 不期而遇奖励后的同步卡顿

现象：

- 第一次在不期而遇里点击选项并发放 reward 时，会出现一次轻微卡顿
- 随后才切回 route 界面

初步判断：

- `RewardResource.Execute -> CardDeckFacade.AddCard` 当前是同步写入牌库数据
- 首次触发时还会懒加载卡牌资源与牌库 Pack
- 这条路径在当前帧内完成，因此会有短暂停顿

结论：

- 目前不优先优化
- 等卡牌系统整体更完整后，再做预热或拆帧处理
