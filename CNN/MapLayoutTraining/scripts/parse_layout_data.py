"""
布局模型 — 数据解析脚本
从经典推箱子关卡提取 (W,H,n) → (箱位置图, 目标位置图) 训练样本。

训练输入: [2, 20, 20]  — ch0=n/MAX_N 常数, ch1=valid_mask
训练标签: [2, 20, 20]  — ch0=箱二值图, ch1=目标二值图
筛选条件: 窗口内 箱数 == 目标数 >= 1 且 <= MAX_N

用法: python parse_layout_data.py [--max-per-size N] [--max-samples N]
"""

import os, sys, re, argparse
import numpy as np

# ── 常量 ──────────────────────────────────────────────────
MAX_SIZE  = 20          # 最大窗口边长
MAX_N     = 5           # 最大箱点对数
CHAR_TO_ID = {
    '#': 1,             # Wall
    ' ': 2,             # Floor
    '@': 2,             # Player → Floor
    '.': 3,             # Target
    '+': 3,             # Player on Target → Target
    '$': 4,             # Box
    '*': 6,             # Box on Target
}

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
LEVEL_DIR   = os.path.join(_SCRIPT_DIR, "../../../Sokoban_Classic_Reference/3rdParty/Levels")
OUT_DIR     = os.path.join(_SCRIPT_DIR, "../data")


# ── 关卡解析 ─────────────────────────────────────────────
def parse_levels(filepath):
    """解析 .txt/.xsb 文件，返回 list of 2D int grids"""
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

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


def rectify(grid):
    """统一宽度 — 短行右侧补地板(ID 2)"""
    h = len(grid)
    w = max(len(row) for row in grid)
    rect = np.full((h, w), 2, dtype=np.int32)
    for i, row in enumerate(grid):
        rect[i, :len(row)] = row
    return rect


# ── 窗口采样 ─────────────────────────────────────────────
def count_boxes_and_goals(window):
    """返回 (box_count, goal_count)"""
    n_box  = ((window == 4) | (window == 6)).sum()
    n_goal = ((window == 3) | (window == 6)).sum()
    return n_box, n_goal


def make_sample(window, n):
    """从 (h, w) 窗口构造 (X_2ch, y_2ch) — 未增强未 pad 的原始尺寸"""
    h, w = window.shape
    X = np.zeros((2, h, w), dtype=np.float32)
    X[0] = n / MAX_N          # 归一化箱点对数
    X[1] = 1.0                # valid mask（pad 前全为 1）

    y = np.zeros((2, h, w), dtype=np.float32)
    y[0] = ((window == 4) | (window == 6)).astype(np.float32)  # 箱
    y[1] = ((window == 3) | (window == 6)).astype(np.float32)  # 目标

    return X, y


def augment_and_pad(X, y):
    """1 个样本 → 8 份增强 + pad 到 (2, 20, 20)"""
    results = []
    for k in range(4):
        X_rot = np.rot90(X, k=k, axes=(1, 2))
        y_rot = np.rot90(y, k=k, axes=(1, 2))

        for flip in [False, True]:
            X_var = np.flip(X_rot, axis=2) if flip else X_rot
            y_var = np.flip(y_rot, axis=2) if flip else y_rot

            _, h, w = X_var.shape
            # pad 到 20×20 — valid mask 自然标记有效区
            X_pad = np.zeros((2, MAX_SIZE, MAX_SIZE), dtype=np.float32)
            y_pad = np.zeros((2, MAX_SIZE, MAX_SIZE), dtype=np.float32)
            X_pad[:, :h, :w] = X_var
            y_pad[:, :h, :w] = y_var
            results.append((X_pad, y_pad))

    return results


# ── 主流程 ───────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser(description="布局模型数据解析")
    parser.add_argument('--max-per-size', type=int, default=3,
                        help='每个 (level, H, W) 组合最多采几个位置 (default: 3)')
    parser.add_argument('--max-samples', type=int, default=0,
                        help='增强后总样本上限，0=不限制 (default: 0)')
    parser.add_argument('--seed', type=int, default=42,
                        help='随机种子 (default: 42)')
    args = parser.parse_args()

    np.random.seed(args.seed)
    os.makedirs(OUT_DIR, exist_ok=True)

    files = sorted(f for f in os.listdir(LEVEL_DIR)
                   if f.endswith('.txt') or f.endswith('.xsb'))

    print(f"布局模型 — 数据解析")
    print(f"  窗口范围: 5~{MAX_SIZE} × 5~{MAX_SIZE}")
    print(f"  Max N: {MAX_N}")
    print(f"  筛选: box == goal >= 1")
    print(f"  每 (level,size) 采样: ≤{args.max_per_size}")
    print(f"  样本上限: {'无' if args.max_samples == 0 else args.max_samples}")
    print(f"  关卡文件: {len(files)}")
    print("-" * 60)

    X_list, y_list = [], []
    n_dist = {i: 0 for i in range(1, MAX_N + 1)}  # N 分布统计
    total_raw = 0

    for fname in files:
        fpath = os.path.join(LEVEL_DIR, fname)
        levels = parse_levels(fpath)
        n_before = len(X_list)

        for grid in levels:
            rect = rectify(grid)
            h, w = rect.shape

            for wh in range(5, min(MAX_SIZE + 1, h + 1)):
                for ww in range(5, min(MAX_SIZE + 1, w + 1)):
                    max_i = h - wh
                    max_j = w - ww
                    if max_i < 0 or max_j < 0:
                        continue

                    total_positions = (max_i + 1) * (max_j + 1)
                    n_pos = min(args.max_per_size, total_positions)

                    # 不重复随机采样位置
                    positions = set()
                    attempts = 0
                    while len(positions) < n_pos and attempts < n_pos * 5:
                        positions.add((
                            np.random.randint(0, max_i + 1),
                            np.random.randint(0, max_j + 1)
                        ))
                        attempts += 1

                    for i, j in positions:
                        window = rect[i:i + wh, j:j + ww]
                        n_box, n_goal = count_boxes_and_goals(window)
                        if n_box == n_goal and 1 <= n_box <= MAX_N:
                            X_raw, y_raw = make_sample(window, n_box)
                            total_raw += 1
                            n_dist[n_box] += 1
                            for X_pad, y_pad in augment_and_pad(X_raw, y_raw):
                                X_list.append(X_pad)
                                y_list.append(y_pad)

                            # 提前截断
                            if args.max_samples > 0 and len(X_list) >= args.max_samples:
                                break
                    if args.max_samples > 0 and len(X_list) >= args.max_samples:
                        break
                if args.max_samples > 0 and len(X_list) >= args.max_samples:
                    break
            if args.max_samples > 0 and len(X_list) >= args.max_samples:
                break

        n_file = len(X_list) - n_before
        print(f"  {fname:<25s} → {len(levels):>4d} 关, {n_file:>8d} 增强样本 "
              f"(累计 {len(X_list):,})", flush=True)

        if args.max_samples > 0 and len(X_list) >= args.max_samples:
            print(f"  ⚠ 已达样本上限 {args.max_samples}，停止解析")
            break

    print("-" * 60)

    if len(X_list) == 0:
        print("❌ 未找到任何满足条件的窗口！")
        return

    # ── 打乱 ─────────────────────────────────────────
    idx = np.random.permutation(len(X_list))
    X_arr = np.array(X_list, dtype=np.float32)[idx]
    y_arr = np.array(y_list, dtype=np.float32)[idx]
    del X_list, y_list  # 释放中间内存

    print(f"\n原始窗口 (增强前): {total_raw:,}")
    print(f"增强后样本: {len(X_arr):,}")
    print(f"X shape: {X_arr.shape}")
    print(f"y shape: {y_arr.shape}")

    # ── 分布统计 ─────────────────────────────────────
    print("\nN 分布 (箱点对数 x8增强):")
    for n in range(1, MAX_N + 1):
        c = (X_arr[:, 0, 0, 0].round(2) == n / MAX_N).sum()
        if c > 0:
            print(f"  n={n}: {c:>8d} ({c / len(X_arr) * 100:5.1f}%)")

    # 有效面积分布
    areas = X_arr[:, 1].sum(axis=(1, 2)).astype(int)
    print(f"\n有效面积 (valid_mask sum):")
    print(f"  min={areas.min()}, max={areas.max()}, mean={areas.mean():.1f}")

    # ── 保存 ─────────────────────────────────────────
    data_path = os.path.join(OUT_DIR, 'data.npz')
    np.savez_compressed(data_path, X=X_arr, y=y_arr)

    size_mb = os.path.getsize(data_path) / 1024 / 1024
    raw_mb = (X_arr.nbytes + y_arr.nbytes) / 1024 / 1024
    print(f"\n已保存 (原始 {raw_mb:.1f} MB, 压缩后 {size_mb:.1f} MB):")
    print(f"  → {data_path}")

    # ── 日记 ─────────────────────────────────────────
    from datetime import datetime
    diary_path = os.path.join(OUT_DIR, 'diary.txt')
    with open(diary_path, 'w', encoding='utf-8') as f:
        f.write(f"生成时间: {datetime.now():%Y-%m-%d %H:%M:%S}\n")
        f.write(f"数据来源: 经典推箱子关卡 ({len(files)} 文件)\n")
        f.write(f"窗口范围: 5~{MAX_SIZE} × 5~{MAX_SIZE}\n")
        f.write(f"筛选: box == goal >= 1, max_n={MAX_N}\n")
        f.write(f"每 (level,size) 最大采样: {args.max_per_size}\n")
        f.write(f"增强: 旋转×4 × 镜像×2 = ×8\n")
        f.write(f"输入: [N, 2, {MAX_SIZE}, {MAX_SIZE}] (ch0=n/{MAX_N}, ch1=mask)\n")
        f.write(f"标签: [N, 2, {MAX_SIZE}, {MAX_SIZE}] (ch0=箱, ch1=目标)\n")
        f.write(f"原始窗口: {total_raw:,}\n")
        f.write(f"增强后样本: {len(X_arr):,}\n")
        f.write(f"文件: data.npz (原始 {raw_mb:.1f} MB, 压缩后 {size_mb:.1f} MB)\n")
        f.write(f"来源文件: {', '.join(files)}\n")
    print(f"  diary → {diary_path}")


if __name__ == '__main__':
    main()
