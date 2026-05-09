# T06 ECS 句柄版本号碰撞修复

## 问题现象

重建关卡（地图尺寸变化）后，玩家输入（WASD 移动、Q 攻击）随机作用到 Box 等其他单位身上。

概率触发，不是必现。

## 根因定位

### EntityHandle 验证机制

`EntitySystem` 使用 `{Id, Version}` 句柄标识实体，`IsValid` 通过比较 `_idVersions[id] == handle.Version` 判断句柄是否仍然有效。

实体销毁时 `_idVersions[id]++`，旧句柄自然失效。

### 触发链条

`EntitySystem.Initialize()` 有两个分支：

| 分支 | 触发条件 | 版本号处理 |
|---|---|---|
| **重分配** | 首次初始化 / 地图尺寸变化 / 数组为空 | new 新数组，`_idVersions[i] = 1`（硬编码） |
| **非重分配** | 容量不变的重建 | `_idVersions[i]++`（递增） |

非重分配路径用 `++`，旧句柄版本号 < 新版本号 → 安全。

**重分配路径用 `= 1` 导致 bug**：

```
第 1 次游戏（触发重分配）：
  _idVersions[0] = 1
  玩家拿到 handle(0, 1)
  UserInputReader 缓存了 _playerHandle = handle(0, 1)

第 2 次游戏（地图尺寸变化 → 重分配）：
  _idVersions = new int[maxEntityCount]
  _idVersions[0] = 1          ← 重置回 1
  第一个创建的实体（例如 Box）分配到 id=0, version=1 → handle(0, 1)

  UserInputReader.TryResolvePlayer():
    IsValid(handle(0, 1)) → _idVersions[0] (1) == 1 → true！
    handle(0, 1) 现在指向的是 Box，不是 Player！
    玩家指令打到了 Box 身上
```

### 为什么概率触发

只有**触发重分配路径**（地图尺寸变化）时版本号才会碰撞。尺寸不变的重建走非重分配路径（`++`），旧句柄自然失效，不会触发。

## 修复方案

三管齐下，从根因到防御层层封堵。

### 修复 1：根因 — 重分配路径使用递增版本号种子

`EntitySystem` 新增 `_versionEpoch` 计数器，每次重分配时递增并用作版本号基值：

```csharp
// EntitySystem.cs — Fields
private int _versionEpoch;

// Initialize() — 重分配路径
for (int i = 0; i < maxEntityCount; i++)
{
    _freeIds.Enqueue(i);
    _idVersions[i] = _versionEpoch;   // 不再是硬编码的 1
    _idToDataIndex[i] = -1;
}
_versionEpoch++;                       // 每次重分配递增
```

效果：

```
第 1 次重分配 → _versionEpoch=1 → 所有实体 version=1
第 2 次重分配 → _versionEpoch=2 → 所有实体 version=2
                                   旧 handle(?, 1) 全部失效 ✅
```

### 修复 2：防御性 — TryResolvePlayer 追加 EntityType 检查

`UserInputReader` 和 `HandZone` 的 `TryResolvePlayer` 中，`IsValid` 通过后二次确认实体类型：

```csharp
if (EntitySystem.Instance.IsValid(_playerHandle))
{
    int idx = EntitySystem.Instance.GetIndex(_playerHandle);
    if (idx >= 0 && entities.coreComponents[idx].EntityType == EntityType.Player)
    {
        playerHandle = _playerHandle;
        return true;
    }
    // 句柄有效但指向了非玩家实体 → 缓存失效，重新遍历
    _playerHandle = EntityHandle.None;
}
// 继续遍历查找真正的 Player
```

### 修复 3：清理残留 — 重建时清空 IntentSystem

`LevelPlayer.BuildEntitiesInternal()` 中，`entitySystem.Initialize()` 之后立即清理意图系统的旧句柄缓存：

```csharp
entitySystem.Initialize(maxEntityCount, _level.width, _level.height);
IntentSystem.Instance?.Clear();    // ← 新增
```

## 涉及文件

| 文件 | 修改内容 |
|---|---|
| `Assets/Scripts/Stage/EntitySystem.cs` | 新增 `_versionEpoch` 字段，重分配路径使用 `_versionEpoch` |
| `Assets/Scripts/LevelPlayer.cs` | `BuildEntitiesInternal` 中调用 `IntentSystem.Instance?.Clear()` |
| `Assets/Scripts/Stage/UserInputReader.cs` | `TryResolvePlayer` 追加 `EntityType` 二次验证 |
| `Assets/Scripts/HandZone.cs` | `TryResolvePlayer` 追加 `EntityType` 二次验证 |

## 维护原则

1. **重分配路径不要用固定值初始化版本号** — 任何从固定值开始的版本号都可能和旧句柄碰撞。应使用递增计数器。
2. **句柄验证提供防御纵深** — `IsValid` 只保证 id/version 匹配，不保证语义正确。缓存句柄的使用者应自行校验类型。
3. **ECS 重建时清理所有引用方** — `Initialize` 重建 ECS 后，所有缓存了旧句柄的系统都应被告知或自行失效。
