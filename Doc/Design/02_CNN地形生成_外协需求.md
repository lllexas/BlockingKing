# CNN 地形生成 — 外协需求

## 目标

训练一个小型 CNN，输入经典推箱子关卡，学习 tile 的空间分布规律。然后用它来生成带球关卡中"房间内部"的地形（在确定的城市-道路骨架内填充）。

---

## 数据源

### 位置

```
G:/ProjectOfGame/BlockingKing/Sokoban_Classic_Reference/3rdParty/Levels/
  ├── Microban.txt       (155 关, 入门级, 推荐主力)
  ├── Microban II.txt    (135 关)
  ├── Microban III.txt   (101 关)
  ├── Original.xsb       (原版 FC 推箱子)
  └── Sasquatch.txt      (高难, 可选)
```

### 格式

标准推箱子文本格式，每个字符代表一个 tile：

| 字符 | 含义 | 映射 ID |
|------|------|---------|
| `#` | 墙 (Wall) | 1 |
| ` ` | 地板 (Floor) | 2 |
| `.` | 目标点 (Target) | 3 |
| `$` | 箱子 (Box) | 4 |
| `@` | 玩家 (Player) | 5 |
| `*` | 箱子在目标上 | 6 |
| `+` | 玩家在目标上 | 7 |

关卡之间用空行分隔。

### 预处理

```
1. 解析文本关卡 → int[,] 矩阵
2. 数据增强（可选）：
   - 90° 旋转 ×4
   - 镜像 ×2
   - 共 ×8 倍训练样本
3. 从每个关卡提取所有 N×N 窗口 → (X, y) 样本对
   X = N×N 窗口内 tile ID（one-hot 编码）
   y = 窗口中心格子的 tile ID（分类标签）
```

---

## 网络架构

### 要求

- 全在 C# 纯 CPU 推理，不用 ComputeShader
- 参数量控制在 **5000 以内**，一次前向 < 1ms
- 输出格式直接可塞进 ScriptableObject，Unity 端加载即用

### 建议架构

```
输入: 9×9×1 (单通道, tile ID 归一化到 [0,1])

Conv1:  3×3 kernel, 1→8 通道, stride=1, pad=valid
        输出: 7×7×8
        ReLU

Conv2:  3×3 kernel, 8→16 通道, stride=1, pad=valid  
        输出: 5×5×16
        ReLU

Flatten → 5×5×16 = 400

Dense1: 400 → 64, ReLU
Dense2: 64 → 10 (10 类 tile 的 logits)

Softmax → 10 类概率分布

参数量估算:
  Conv1: 3×3×1×8 + 8 = 80
  Conv2: 3×3×8×16 + 16 = 1168
  Dense1: 400×64 + 64 = 25664 → ✗ 太大了
  
缩减方案:
  Conv1 后 MaxPool 2×2 → 3×3×8 = 72
  或把窗口缩到 7×7, Conv 后更小

推荐最终架构:
  输入: 7×7×1
  Conv1: 3×3, 1→8  → 5×5×8   (参数 80)
  Conv2: 3×3, 8→8  → 3×3×8   (参数 584)
  Flatten → 72
  Dense: 72 → 10               (参数 730)
  Softmax
  
  总参数 ≈ 1394, 纯 C# 无压力
```

### 窗口大小说明

```
3×3: 只能看邻接关系，学不到空间结构 → 不推荐
5×5: 能看到小凹室和柱子 → 勉强
7×7: 能看到完整的房间区域 → 推荐
9×9: 信息更多但参数暴增 → 需要 MaxPool
```

建议用 **7×7** 起步，效果好就停，不够再上 9×9+Pool。

---

## 训练

### 环境

```bash
pip install torch numpy
```

纯 CPU 训练即可，不需要 GPU。数据集小（几千到几万样本），几分钟完事。

### 训练要点

```
损失函数: CrossEntropyLoss
优化器: Adam, lr=0.001
Epochs: 50~100
Batch: 32
验证: 随机 10% 样本做验证集

数据增强: 旋转 90°/180°/270° + 水平翻转 → ×8
```

### 输出

```
1. 权重文件（二进制）:
   conv1_weight.bin, conv1_bias.bin
   conv2_weight.bin, conv2_bias.bin
   dense_weight.bin, dense_bias.bin

2. Unity ScriptableObject:
   CNNModel.asset — 包含所有权重数据
```

---

## Unity 端推理

### 要求

- C# 纯实现，不依赖外部 DLL
- 权重从 ScriptableObject 读取
- 一个静态方法：`int[] GenerateTile(int[,] context)` 
  输入一个 7×7 的上下文（部分已确定的 tile），输出中心格子的 tile 概率分布

### 接口

```csharp
[CreateAssetMenu]
public class CNNModel : ScriptableObject
{
    // Conv1
    public float[,,,] conv1Weight; // [3,3,1,8]
    public float[] conv1Bias;       // [8]
    
    // Conv2
    public float[,,,] conv2Weight; // [3,3,8,8]
    public float[] conv2Bias;       // [8]
    
    // Dense
    public float[,] denseWeight;    // [72, 10]
    public float[] denseBias;       // [10]
    
    // 推理
    public float[] Predict(float[,] input7x7);
    // 返回 10 个 tile 概率
}
```

### 集成到 WFC 生成管线

```
WFC 循环中，每次需要确定一个格子时：
  1. 取该格子周围的 7×7 上下文（已确定格=真实tile，未确定=0）
  2. 调用 CNNModel.Predict(context)
  3. 按概率采样 → 确定该格子的 tile
  4. 继续传播
```

---

## 交付物清单

- [ ] 数据解析脚本（.txt → 训练样本）
- [ ] 训练脚本（PyTorch）
- [ ] 训练好的权重文件
- [ ] 权重到 ScriptableObject 的导入脚本 / 手动配置说明
- [ ] Unity C# 推理代码（CNNModel.cs）
- [ ] 简单测试：输入几个已知上下文，检查输出是否合理

---

## 训练粒度

注意：网络学的是**房间内部**的地形。训练时不需要区分"走廊"和"房间"——数据集里的经典关卡本身就同时包含开阔空间和狭窄通道，网络自然会学到两者。

如果效果不够好，可以考虑**分段训练**：先把经典关卡按"房间"和"走廊"人工标注分割，分别训练两个模型。但先试统一模型。
