# T07 Target Beacon 角点信标变体维护指南

## 目标

`TerrainDrawSystem.RebuildRuntimeTargetMeshes` 在 Tag 格子四角绘制信标，并用**邻接变体**避免多个同种 Tag 相邻时视觉杂乱。

核心思想：

```text
孤立的 Tag → L 形拐角信标（两段竖直鳍片形成角标）
相邻的 Tag → 变体降级为短鳍片（仍竖直向上，只是更细更短）
全包围的 Tag → 微弱小 DOT（几乎不可见）
```

所有变体都**从地面竖直向上发射**，没有贴地的扁平元素。

## 入口与数据流

```text
LateUpdate
  → RebuildTargetMatrices        （收集哪些格子有 Tag）
  → RebuildRuntimeTargetMeshes   （调用 AddTargetCellQuads）
    → AddTargetCellQuads          （中心方块 + 四角 AddCornerGlyph）
      → AddCornerGlyph            （查邻接，选变体）
```

关键数据结构：

- `sameKindTargets: HashSet<Vector2Int>` — 所有同种 Tag 的格子坐标集合
- 六个方向的偏移在 `AddTargetCellQuads` 中硬编码，一个格子四个角分别调用

## AddCornerGlyph 六种变体

每个角检查三个邻接方向：水平邻格、垂直邻格、对角邻格，判断条件为 `sameKindTargets.Contains(cell + offset)`。

### 变体总表

| 条件 | 视觉效果 | 高度 | 长度 | 厚度 | 不透明度 |
|---|---|---|---|---|---|
| **全包围** `h&&v&&d` | 缩小 DOT 信标 | 28% | — | 75% | 18% |
| **双边** `h&&v` | 对角线短鳍片 | 50% | 55% | 50% | 100% |
| **水平邻接** `h` | 水平方向短鳍片 | 50% | 40% | 50% | 100% |
| **垂直邻接** `v` | 垂直方向短鳍片 | 50% | 40% | 50% | 100% |
| **对角邻接** `d` | 对角线弱化鳍片 | 35% | 30% | 40% | 55% |
| **孤立**（无邻接） | L 形小拐角 | 100% | `80%`（每臂） | 100% | 100% |

### 方向规则

鳍片方向**平行于邻接方向**，而不是垂直于它。这表示两个 Tag 之间的"连接"而非"截断"：

```text
h（水平邻接） → 沿 inwardHorizontal 方向延伸
v（垂直邻接） → 沿 inwardVertical 方向延伸
h&&v          → 沿对角线 (inwardH + inwardV).normalized 方向延伸
d（对角邻接） → 沿对角线方向（弱化）
```

以格子的**左下角**（corner=`(0,0,0)`）为例：

| 方向参数 | 值 | 含义 |
|---|---|---|
| `inwardHorizontal` | `Vector3.right` | 朝 +X（进入格子） |
| `inwardVertical` | `Vector3.forward` | 朝 +Z（进入格子） |
| `horizontalNeighbor` | `Vector2Int.left` | 左邻格 |
| `verticalNeighbor` | `Vector2Int.down` | 下邻格 |

### 形状定义

**DOT**: `AddCornerBeacon` — 四片竖直 Quad 组成方形截面柱，从地面升到 `beaconHeight`。

**ShortLine（鳍片）**: `AddVerticalShortLine` — 三片 Quad 组成的竖直鳍片：
- 左竖面（法向朝 `-perp`）
- 右竖面（法向朝 `+perp`）
- 顶面（法向朝上）

## 关键参数

位于 `TerrainDrawSystem` Inspector：

| 参数 | 默认值 | 作用 |
|---|---|---|
| `targetBeaconHeight` | 1.8 | DOT 信标高度（单位：格子倍数） |
| `targetBeaconWidthRatio` | 0.03 | DOT / 鳍片基准粗细（相对于格子尺寸） |
| `targetCornerBracketRatio` | 0.28 | 鳍片基准长度（相对于格子尺寸） |
| `coreTargetBeaconHeightMultiplier` | 1.85 | Core Target 信标额外高度倍率 |

鳍片的实际尺寸由 `AddCornerGlyph` 内的倍率控制（见变体总表）。

## 颜色系统

所有 Target 类型的颜色统一从 `TileMappingConfig` 的 tag 表中读取，不依赖硬编码或 `_targetTags[0]`：

| Tag 名 | config 颜色 | 含义 |
|---|---|---|
| `Target` | 黄色 `(0.89, 0.59, 0.21)` | 普通 Target 信标 |
| `Target.Core` | 蓝色 `(0.12, 0.55, 1.0)` | Core Target 信标 |
| `Target.Enemy` | 红色 `(0.55, 0.04, 0.08)` | Enemy Target 信标 |

查找方法：`ResolveTagColor(string tagName)` 遍历 `_config.AllTagIDs`，按名称匹配返回颜色。

唯一保持 `[SerializeField]` 硬编码的是 `completedTargetColor`（绿色）——因为它不是一种 tag 类型，而是"箱子已在 Target 上"的状态色。

## 常见错误

### 方向写反

`h` 和 `v` 的方向容易搞混：

```
正确：h → inwardHorizontal,  v → inwardVertical
错误：h → inwardVertical,     v → inwardHorizontal  ← 变成"截断"风格
```

检查方法：两个水平相邻的 Tag，它们之间的鳍片应该**水平延伸**（平行于邻接边），而不是垂直伸进格子里。

### 贴地线

`AddVerticalShortLine` 必须替代 `AddFloorLine`。如果有新变体需要新增，**不要使用贴在 XZ 平面上的 Quad**，所有角点标记都必须有高度。

## 文件位置

- 变体逻辑：`TerrainDrawSystem.cs` 的 `AddCornerGlyph`（约 339-394 行）
- 鳍片生成：`TerrainDrawSystem.cs` 的 `AddVerticalShortLine`（约 396-420 行）
- DOT 信标：`TerrainDrawSystem.cs` 的 `AddCornerBeacon`（约 555-566 行）—— 仅用于全包围（h&&v&&d）弱化标记
- 拐角 L-shape：`AddCornerGlyph` 孤立角分支（约 391-395 行）—— 两段 `AddVerticalShortLine` 组成
