# 快速播放（Quick Play）调用链与 Domain Reload 机制

**日期**：2026-05-08  
**标签**：Unity Editor, Play Mode, Domain Reload, Scene Management  
**涉及文件**：`Assets/Editor/LevelDataInspector.cs`, `Assets/Scripts/LevelPlayer.cs`, `Assets/Scripts/QuickPlaySession.cs`

---

## 1. 需求背景

在关卡编辑器中，选中一个 `LevelData` SO 后点「快速播放」，期望：
- 保存当前编辑场景状态
- 切换到 StageScene 进入 Play Mode
- LevelPlayer 构建关卡 Mesh 并显示
- 退出 Play Mode 后恢复原编辑场景

---

## 2. 核心问题：Domain Reload

Unity Play Mode 的默认设置为 "Enter Play Mode Options" → **Reload Domain**（开启）。

Domain Reload 的行为：摧毁当前 C# 域（Editor Domain），新建一个 C# 域（Play Domain）。

**变化**：
| 资源 | Enter Play Mode 时 | Exit Play Mode 时 |
|------|-------------------|-------------------|
| 静态 C# 字段 | ❌ 归零 | ❌ 归零 |
| 静态事件订阅 | ❌ 清空 | ❌ 清空 |
| `EditorPrefs` | ✅ 存活 | ✅ 存活 |
| Unity C++ 原生层 | ✅ 存活 | ✅ 存活 |
| `playModeStartScene` | ✅ C++ 层已缓存 | ❌ 不适用 |

---

## 3. 三个关键设计决策

### 3.1 EditorPrefs 替代静态字段

```csharp
// ❌ 错误：Domain reload 后归零
private static string _quickPlayOriginPath;

// ✅ 正确：写入注册表，跨 Domain 存活
EditorPrefs.SetString("QuickPlay_OriginPath", scenePath);
```

### 3.2 [InitializeOnLoad] 替代方法内订阅

```csharp
// ❌ 错误：QuickPlay() 里 +=，Domain reload 后丢失
EditorApplication.playModeStateChanged += OnStateChanged;

// ✅ 正确：静态构造器，每次 Assembly 加载时重建订阅
[InitializeOnLoad]
private static class Tracker {
    static Tracker() {
        EditorApplication.playModeStateChanged += OnStateChanged;
    }
}
```

### 3.3 ExitingPlayMode 清空 playModeStartScene

```csharp
// ✅ 最安全时机：退出 Play Mode 时清空
case PlayModeStateChange.ExitingPlayMode:
    EditorSceneManager.playModeStartScene = null;
    break;

// ⚠️ 不安全：ExitingEditMode 时 Unity 已缓存，此时清空不影响本次
// 但不标准，容易造成维护误解
```

---

## 4. 完整调用链

```
QuickPlay()
  │
  ├─ [步骤1] EditorPrefs 写入 origin 路径 + SceneSetup JSON
  ├─ [步骤2] QuickPlaySession.asset 写入 LevelData + Config
  ├─ [步骤3] playModeStartScene = StageScene（C++ 层）
  └─ [步骤4] EnterPlaymode()
      │
      ├─ ExitingEditMode
      │   → Tracker.OnStateChanged 收到
      │   → Unity C++ 此时已缓存 playModeStartScene
      │   → 仅日志，不做任何清除
      │
      ├─ [Domain Reload]
      │   → C# 静态字段归零，事件清空
      │   → [InitializeOnLoad] 静态构造器重建订阅
      │   → EditorPrefs（注册表）存活
      │   → playModeStartScene（C++ 缓存）存活
      │
      ├─ Unity 查询 playModeStartScene → StageScene
      ├─ 加载 StageScene
      │
      ├─ EnteredPlayMode（Tracker 已重建，收到）
      │
      ├─ LevelPlayer.Start()
      │   → 读 QuickPlaySession
      │   → BuildMesh() 构建关卡
      │   → 显示几何网格
      │
      └─ [用户 Stop]
          │
          ├─ ExitingPlayMode（Tracker 收到）
          │   → EditorSceneManager.playModeStartScene = null
          │   → 防止下次正常 Play 也加载 StageScene
          │
          ├─ [Domain Reload（退出 Play Mode）]
          │   → 同上，[InitializeOnLoad] 重建订阅
          │
          ├─ Unity C++ 自动恢复编辑场景快照
          │   → ExitingEditMode 时 Unity 内部保存了快照
          │   → 此时自动恢复，不需要 C# 代码干预
          │
          └─ EnteredEditMode（Tracker 收到）
              → 读 EditorPrefs 找 origin
              → RestoreSceneManagerSetup 确保多场景布局正确
              → 清理 EditorPrefs
```

---

## 5. 关键原理

### 5.1 playModeStartScene 的读取时机

Unity C++ 层在 **触发 ExitingEditMode 之前** 就已经读取了 `playModeStartScene` 的值并内部缓存。所以：

```csharp
EditorSceneManager.playModeStartScene = stageAsset;  // ← 这里设置
EditorApplication.EnterPlaymode();
// ← Unity C++ 已经缓存了 stageAsset
// ← ExitingEditMode 才触发，此时清空也不影响
```

### 5.2 Unity 自动恢复编辑场景

即使在 ExitingEditMode → Domain reload → EnteredEditMode 期间 C# 代码全部丢失，Unity C++ 层也在 ExitingEditMode 时自动保存了**编辑器场景状态快照**。退出 Play Mode 后自动恢复，不需要 C# 代码干预。

### 5.3 SceneSetup 序列化

`EditorSceneManager.GetSceneManagerSetup()` 返回 `SceneSetup[]`——包含每个加载场景的 path、isActive、isLoaded。

通过 `JsonUtility.ToJson()` 序列化为字符串存入 EditorPrefs，退出 Play Mode 后反序列化恢复。

---

## 6. 对比：旧代码为什么无效

| 组件 | 旧实现 | 问题 |
|------|--------|------|
| 状态存储 | 静态字段 | Domain reload 归零 |
| 事件订阅 | QuickPlay() 内 += | Domain reload 后丢失 |
| 恢复时机 | EnteredEditMode 回调 | 回调丢失，恢复代码永不到达 |
| playModeStartScene 清空 | ExitingEditMode | 时机不标准，误导后续维护 |

旧代码完全依赖 Unity C++ 层的自动恢复兜底，我们的恢复逻辑全是死代码。

---

## 7. 模式总结

```
跨 Domain Reload 的方案选择优先级：

1. Unity C++ 原生 API   → 如 playModeStartScene（不受 domain 影响）
2. 持久化存储           → EditorPrefs、PlayerPrefs、文件
3. [InitializeOnLoad]   → Assembly 加载时自动重建状态
4. 静态字段/事件        → 只在当前 Domain 有效，domain reload 后清零
```

### 本方案中三层各司其职：

- **EditorPrefs（文件层）**：持久化 origin 场景信息
- **[InitializeOnLoad]（脚本层）**：跨 Domain 重建事件订阅
- **Unity C++ 原生层**：自动缓存 playModeStartScene、自动保存编辑场景快照
