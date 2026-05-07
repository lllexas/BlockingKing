# TileMap 编辑器工具设计方案

## 概述

在 Unity 场景中用 Tilemap 摆关卡，通过编辑器工具一键 Bake 成 ScriptableObject（LevelData），运行时读取 SO 即可重建关卡数据。渲染逻辑单独处理，本工具只负责产出 **int[][] 格式的关卡数据**。

---

## 数据流

```
[Unity 场景: 在 Tilemap 上摆砖块]
        ↓
[LevelBakerWindow] — 选中 Tilemap + 配置，点击 Bake
        ↓
[TileMappingConfig] — TileBase → int ID 映射
        ↓
[LevelData SO] — 产出关卡数据资源文件
        ↓
[运行时: 读取 LevelData → 交给渲染层]
```

---

## 核心数据类型

### 1. LevelData (ScriptableObject)

```csharp
[CreateAssetMenu]
public class LevelData : ScriptableObject
{
    public string levelName;        // 关卡名称
    public int width;               // 地图宽度
    public int height;              // 地图高度
    public int[] tiles;             // 一维数组, index = y * width + x
    [OdinSerialize] public int[][] tiles2D; // 二维数组视图 (Odin 辅助)
}
```

- **tiles** — 一维 int[]，主要存储格式
- **tiles2D** — 用 Odin 序列化的二维数组，用于 Inspector 中直观查看/编辑
- 两者互相同步（Bake 时写入一维，需要时转为二维）

### 2. TileMappingConfig (ScriptableObject)

```csharp
[CreateAssetMenu]
public class TileMappingConfig : ScriptableObject
{
    public List<TileMappingEntry> entries;
}

[System.Serializable]
public class TileMappingEntry
{
    public TileBase tileAsset;  // Unity Tile 资源
    public int tileID;          // 对应的整型 ID
    public Color previewColor;  // 在 Inspector 预览颜色
}
```

约定：
- **ID = 0** → 空（无砖块）
- **ID ≥ 1** → 具体砖块类型

---

## 编辑器工具

### LevelBakerWindow (EditorWindow)

通过菜单 `Tools/推箱子/关卡烘焙机` 打开。

**Inspector 面板：**

| 字段 | 类型 | 说明 |
|------|------|------|
| Level ID | string | 关卡标识 |
| Tile Mapping Config | TileMappingConfig | 映射配置 |
| Target Tilemap | Tilemap | 场景中的 Tilemap 引用 |
| Output Folder | string | SO 输出目录 |

**Bake 流程：**

1. 读取 Target Tilemap 的 cellBounds
2. 遍历每个格子，通过 TileMappingConfig 将 TileBase → int ID
3. 计算有效区域（去除空行空列，可选）
4. 创建 LevelData SO 并填充数据
5. 保存到项目目录

---

## 推箱子 Tile ID 规划

```
0  = 空（无砖块）
1  = 墙壁 (Wall)
2  = 地板 (Floor)
3  = 目标点 (Target)
4  = 箱子 (Box)
5  = 玩家 (Player)
6  = 箱子在目标点上 (BoxOnTarget)
7  = 玩家在目标点上 (PlayerOnTarget)
```

---

## 渲染隔离原则

本工具**只负责产出 int[][] 数据**，不关心渲染。

渲染层（后续实现）职责：
- 读取 LevelData.tiles
- 遍历 int[][]，根据 ID 实例化对应的 GameObject/Sprite
- 处理动画、光照等

这样分工清晰，编辑器工具一旦写好就不会因渲染方案变化而重做。

---

## 文件结构

```
Assets/
├── Editor/
│   └── LevelBakerWindow.cs      ← 烘焙窗口
├── Scripts/
│   ├── LevelData.cs              ← 关卡数据 SO
│   └── TileMappingConfig.cs      ← Tile 映射配置 SO
├── Settings/
│   └── TileMappingConfig.asset   ← 映射配置实例
├── Levels/                        ← 烘焙产出的关卡 SO 存放目录
│   └── Level_01.asset
└── Tilemaps/                      ← 场景中用到的 TileSet 资源
```

---

## 实现步骤（后续实施）

1. 创建 `LevelData` SO 数据类型
2. 创建 `TileMappingConfig` SO 数据类型
3. 实现 `LevelBakerWindow` 编辑器窗口
4. 配置 TileMappingConfig 实例（注册 Tile 与 ID 的映射）
5. 测试 Bake 流程，确认产出正确的 int[][] 数据
