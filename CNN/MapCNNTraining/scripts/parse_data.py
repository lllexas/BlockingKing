"""
数据解析脚本：从经典推箱子关卡提取 7×7 训练样本。
输出: data/X.npy, data/y.npy
"""

import os
import re
import numpy as np

# ── Tile 映射 ──────────────────────────────────────────────
# @(玩家) → 地板, +(人在目标) → 目标 — 合并为 5 类地形
CHAR_TO_ID = {
    '#': 1,  # Wall
    ' ': 2,  # Floor
    '@': 2,  # Player → Floor
    '.': 3,  # Target
    '+': 3,  # Player on Target → Target
    '$': 4,  # Box
    '*': 6,  # Box on Target
}

# 训练标签映射: tile ID → 分类索引 (0~4)
ID_TO_CLASS = {1: 0, 2: 1, 3: 2, 4: 3, 6: 4}
NUM_CLASSES = 5
WINDOW_SIZE = 7
PAD = WINDOW_SIZE // 2  # = 3

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
LEVEL_DIR = os.path.join(_SCRIPT_DIR, "../../../Sokoban_Classic_Reference/3rdParty/Levels")
OUT_DIR = os.path.join(_SCRIPT_DIR, "../data")


def parse_levels(filepath):
    """解析一个关卡文件，返回 list of 2D int grids."""
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # 空行分隔关卡
    blocks = re.split(r'\n\s*\n', content.strip())

    levels = []
    for block in blocks:
        lines = [line.rstrip() for line in block.split('\n')
                 if not line.strip().startswith(';') and line.strip()]
        if not lines:
            continue

        grid = []
        for line in lines:
            row = [CHAR_TO_ID.get(c, 0) for c in line]
            if row:
                grid.append(row)

        if grid:
            levels.append(grid)

    return levels


def extract_all_windows(levels):
    """从所有关卡提取 7×7 窗口（padding 边缘）。"""
    X_list, y_list = [], []

    for grid in levels:
        h = len(grid)
        # 各行长度不一致 → 统一矩形化，短行右侧补地板 (ID 2)
        w = max(len(row) for row in grid)
        rect = np.full((h, w), 2, dtype=np.int32)
        for i, row in enumerate(grid):
            rect[i, :len(row)] = row

        # 窗口提取 padding: 四周各补 PAD 格，值=0（关卡外部/未知）
        padded = np.zeros((h + 2 * PAD, w + 2 * PAD), dtype=np.int32)
        padded[PAD:PAD + h, PAD:PAD + w] = rect

        for i in range(PAD, h + PAD):
            for j in range(PAD, w + PAD):
                tile_id = padded[i, j]
                if tile_id == 0:
                    continue  # 跳过 padding 格
                window = padded[i - PAD:i + PAD + 1,
                                j - PAD:j + PAD + 1]
                X_list.append(window)
                y_list.append(ID_TO_CLASS[tile_id])

    return np.array(X_list, dtype=np.int32), np.array(y_list, dtype=np.int32)


def augment(X, y):
    """数据增强: 旋转 0°/90°/180°/270° × 水平翻转 × 不翻转 = ×8."""
    X_aug, y_aug = [], []

    for window, label in zip(X, y):
        for k in range(4):
            rot = np.rot90(window, k=k)
            X_aug.append(rot)
            y_aug.append(label)
            X_aug.append(np.fliplr(rot))
            y_aug.append(label)

    return np.array(X_aug, dtype=np.int32), np.array(y_aug, dtype=np.int32)


def class_distribution(y):
    """统计类别分布。"""
    cls_names = {0: '# 墙', 1: '  地板', 2: '. 目标',
                 3: '$ 箱', 4: '* 箱在目标'}
    unique, counts = np.unique(y, return_counts=True)
    total = len(y)
    print("  类别分布:")
    for u, c in zip(unique, counts):
        print(f"    {cls_names.get(u, str(u))}: {c:>8d} ({c/total*100:5.1f}%)")
    print(f"    总计: {total}")


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    level_dir = os.path.join(os.path.dirname(__file__), LEVEL_DIR)
    files = sorted(f for f in os.listdir(level_dir)
                   if f.endswith('.txt') or f.endswith('.xsb'))

    print(f"找到 {len(files)} 个关卡文件")
    print(f"窗口大小: {WINDOW_SIZE}×{WINDOW_SIZE}, 类别数: {NUM_CLASSES}")
    print("-" * 50)

    X_all, y_all = [], []

    for fname in files:
        fpath = os.path.join(level_dir, fname)
        levels = parse_levels(fpath)
        X, y = extract_all_windows(levels)
        print(f"  {fname:<25s} → {len(levels):>4d} 关, {len(X):>7d} 窗口")
        X_all.append(X)
        y_all.append(y)

    print("-" * 50)

    X_all = np.concatenate(X_all)
    y_all = np.concatenate(y_all)
    print(f"\n原始样本: {len(X_all)}")

    # 数据增强
    print("\n进行数据增强 (×8: 旋转4×翻转2) ...")
    X_aug, y_aug = augment(X_all, y_all)
    print(f"增强后样本: {len(X_aug)}")

    # 打乱
    idx = np.random.permutation(len(X_aug))
    X_aug, y_aug = X_aug[idx], y_aug[idx]

    print("\n增强后类别分布:")
    class_distribution(y_aug)

    # 保存
    X_path = os.path.join(OUT_DIR, 'X.npy')
    y_path = os.path.join(OUT_DIR, 'y.npy')
    np.save(X_path, X_aug)
    np.save(y_path, y_aug)
    print(f"\n已保存:")
    print(f"  X → {X_path}  ({X_aug.shape}, {X_aug.dtype})")
    print(f"  y → {y_path}  ({y_aug.shape}, {y_aug.dtype})")

    # 日记
    from datetime import datetime
    diary = os.path.join(OUT_DIR, 'diary.txt')
    with open(diary, 'w', encoding='utf-8') as f:
        f.write(f"生成时间: {datetime.now():%Y-%m-%d %H:%M:%S}\n")
        f.write(f"类别数: {NUM_CLASSES}\n")
        f.write(f"映射: {ID_TO_CLASS}\n")
        f.write(f"char→id: {CHAR_TO_ID}\n")
        f.write(f"窗口: {WINDOW_SIZE}×{WINDOW_SIZE}\n")
        f.write(f"增强: 旋转×4 × 镜像×2 = ×8\n")
        f.write(f"总样本: {len(X_aug)}\n")
        f.write(f"来源: {', '.join(files)}\n")
    print(f"  diary → {diary}")


if __name__ == '__main__':
    main()
