# Intent 结算与光环扳机维护

日期：2026-05-10

## 目标

Stage 战斗规则采用仿炉石的结算思路：

```text
意图只描述“我要做什么”
系统执行意图
意图执行后进入统一结算管线
持续光环先刷新
再做死亡检查
如果死亡改变场面，则重复刷新光环与死亡检查，直到稳定
```

这套规则的核心目的不是复刻炉石全部细节，而是避免散装系统在移动、攻击、刷怪、死亡时各自改状态，导致顺序不可预测。

---

## 当前运行链路

一次 tick 的 Stage 顺序：

```text
EntitySystem.UpdateTick()
  -> GlobalTick++
  -> SpawnSystem.Tick()
       只提交 SpawnIntent，不直接创建敌人
  -> IntentSystem.Tick()
       消费玩家/系统/敌人意图
       执行具体 intent
       执行统一 resolution
  -> EnemyAutoAISystem.Tick()
       写入下一拍敌人意图
```

注意：`SpawnSystem.Tick()` 不能直接修改世界。`Target.Enemy` 是实体，刷怪必须表现为该实体提交 `SpawnIntent`。

---

## Intent 管线

`IntentSystem.ExecuteIntent()` 的职责：

```text
BeforeIntentExecute
执行具体 Intent
AfterIntentExecute
ResolveWorldState
回收 Intent
```

当前具体 intent 路由：

```text
MoveIntent   -> MoveSystem.Execute
AttackIntent -> AttackSystem.Execute
CardIntent   -> CardEffectSystem.Execute
SpawnIntent  -> SpawnSystem.Execute
```

后续可以把这里的 switch 升级成 executor registry，但在升级前不要绕过 `IntentSystem` 直接执行行为。

---

## Resolution 管线

`IntentSystem.ResolveWorldState()` 当前顺序：

```text
IntentResolutionBegin

repeat max 8:
  AuraUpdate
  DeathCheck
  如果本轮没有死亡 -> break

IntentResolutionEnd
```

维护约定：

- 持续属性变化必须走 `AuraUpdate`。
- 实体死亡必须走 `DeathCheck`。
- 攻击和卡牌只记录伤害，不直接删除实体。
- 创建实体后可以发布 `EntityCreated`，但不能依赖它替代 resolution。

---

## StatusComponent 约定

`CoreComponent` 只保存身份、位置、ID、占格信息。

战斗数值放在 `StatusComponent`：

```text
BaseAttack
BaseMaxHealth
AttackModifier
MaxHealthModifier
DamageTaken
Block
```

读取面板必须走 `CombatStats`：

```text
GetAttack
GetMaxHealth
GetCurrentHealth
DealDamage
```

不要直接写“当前血量”。当前血量是计算值：

```text
CurrentHealth = MaxHealthAfterModifiers - DamageTaken
```

这点对光环很重要。例如围棋兵 5/5 被打 4 点伤害后：

```text
BaseMaxHealth = 1
AuraMaxHealthModifier = 4
DamageTaken = 4
CurrentHealth = 1
```

光环刷新不能清空 `DamageTaken`。

---

## 光环模型

光环分三层：

```text
EntityAuraDefinition
  配置在 EntityBP 上，表示“这个单位声明自己拥有某种光环”

AuraSource
  运行时从场上实体收集出来的一条声明

AuraProvider / BatchAuraProvider
  执行具体光环逻辑
```

`EntityBP` 使用 Odin/SerializeReference 多态列表：

```text
EntityBP.auras : List<EntityAuraDefinition>
```

实体创建时，BP 会写入：

```text
PropertyComponent.SourceBP
PropertyComponent.SourceTagId
```

`AuraResolutionSystem` 在 `AuraUpdate` 阶段扫描场上实体，从每个实体的 `SourceBP.auras` 收集 `AuraSource`。

---

## 单体光环与批处理光环

每个光环自己声明是否批处理：

```text
SupportsBatch = false
  每个 AuraSource 单独创建 AuraProvider

SupportsBatch = true
  按 BatchKey 聚合 AuraSource
  创建一个 BatchAuraProvider 统一处理
```

围棋光环是批处理光环：

```text
GoConnectedGroupAuraDefinition
  SupportsBatch = true
  BatchKey = "go.connected_group"
  CreateBatchProvider() -> GoAuraProvider
```

这样语义上仍然是“每个棋子声明自己拥有围棋光环”，实现上由 `GoAuraProvider` 对所有围棋 source 做一次连通块计算。

---

## 围棋光环规则

围棋光环当前实现：

```text
1. AuraResolutionSystem 收集所有声明 GoConnectedGroupAura 的实体作为 sources
2. GoAuraProvider 清理这些 source 的 aura modifier
3. GoAuraProvider 按正交方向计算 source 集合中的连通块
4. 每个棋子的面板设置为 N / N
   N = 所在连通块大小
```

实现位置：

```text
Assets/Scripts/Stage/GoAuraProvider.cs
```

注意：围棋光环只处理 source 列表里的实体，不应该再按 tag 全局扫出“隐式来源”。来源必须来自实体自己的 `EntityBP.auras` 声明。

---

## 事件边界

`EntityCreated` 的规则：

```text
CreateEntity 只分配实体，不发布 EntityCreated
创建方应用 BP / status / tag / 特殊标记后，再调用 PublishEntityCreated
```

原因：半初始化实体会让监听者读到错误的 `SourceTagId`、`SourceBP`、血量或 `IsCore`。

当前发布点：

```text
LevelPlayer.CreateTaggedEntity
LevelPlayer.CreateCoreBox
SpawnSystem.ApplyBP
EntitySystem.TryMaterializeWall
```

---

## 新增光环流程

新增一个普通光环：

```text
1. 继承 EntityAuraDefinition
2. 保持 SupportsBatch = false
3. 实现 CreateProvider(AuraSource source)
4. 继承 AuraProvider，实现 Clear / Apply
5. 在 EntityBP.auras 中添加该 definition
```

新增一个批处理光环：

```text
1. 继承 EntityAuraDefinition
2. override SupportsBatch = true
3. override BatchKey
4. 实现 CreateBatchProvider()
5. 继承 BatchAuraProvider，实现 Clear / Apply
6. 在 EntityBP.auras 中添加该 definition
```

光环不要自己订阅 `EventBusSystem`。光环结算时机由 `IntentSystem.ResolveWorldState()` 统一控制。

---

## 当前限制

1. `AttackModifier` / `MaxHealthModifier` 仍是单槽位。

   多个光环同时修改同一属性时会互相覆盖。后续需要拆成：

   ```text
   AuraAttackModifier
   AuraMaxHealthModifier
   CardAttackModifier
   TerrainAttackModifier
   ```

   或升级为真正的 modifier buffer。

2. `IntentSystem` 仍用 switch 路由 intent。

   后续可以升级成 executor registry，但不要在各系统里绕过 intent 管线。

3. 卡牌冲撞与延迟死亡仍有设计冲突。

   当前死亡在 resolution 中处理。如果某张卡需要“杀死后立刻穿过”，应把卡牌动作拆成多个子步骤，并在子步骤之间执行 resolution，而不是在 `CardEffectSystem` 里直接 `DestroyEntity`。

4. 光环 source 筛选目前主要来自 `EntityBP.auras`。

   如果后续需要更细筛选，可以给 `EntityAuraDefinition` 增加：

   ```text
   CanSource(EntitySystem entitySystem, int entityIndex)
   ```

   但触发时机仍应由 resolution 管线统一控制。

