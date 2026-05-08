"""
布局特征模型 — 数据解析脚本
从经典推箱子关卡提取 (W,H,n) → 箱子/目标特征向量 的训练样本。

每个箱/目标取 6 特征: vec_x, vec_y, dist_left, dist_right, dist_top, dist_bottom
Pad 到 n_max=5，标签: [2, 5, 6] = 60 浮点数
输入: [3] = (W/20, H/20, n/5)

用法: python parse_feature_data.py [--max-per-size N] [--max-samples N]
"""

import os, sys, re, argparse
import numpy as np

# ── 常量 ──────────────────────────────────────────────────
MIN_SIZE   = 5
MAX_SIZE   = 20
MAX_N      = 5
CHAR_TO_ID = {
    '#': 1, ' ': 2, '@': 2, '.': 3, '+': 3, '$': 4, '*': 6,
}

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
LEVEL_DIR   = os.path.join(_SCRIPT_DIR, "../../../Sokoban_Classic_Reference/3rdParty/Levels")
OUT_DIR     = os.path.join(_SCRIPT_DIR, "../data")


# ── 关卡解析 ─────────────────────────────────────────────
def parse_levels(filepath):
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
    h = len(grid)
    w = max(len(row) for row in grid)
    rect = np.full((h, w), 2, dtype=np.int32)
    for i, row in enumerate(grid):
        rect[i, :len(row)] = row
    return rect


# ── 坐标提取 ─────────────────────────────────────────────
def extract_positions(window):
    """提取箱和目标坐标，行列序排列"""
    box_y, box_x = np.where((window == 4) | (window == 6))
    goal_y, goal_x = np.where((window == 3) | (window == 6))

    box_order = np.lexsort((box_x, box_y))
    goal_order = np.lexsort((goal_x, goal_y))

    return (list(zip(box_y[box_order], box_x[box_order])),
            list(zip(goal_y[goal_order], goal_x[goal_order])))


def compute_features(positions, H, W):
    """6 特征: vec_x, vec_y, dist_left, dist_right, dist_top, dist_bottom"""
    cx, cy = (W - 1) / 2.0, (H - 1) / 2.0
    feats = np.zeros((MAX_N, 6), dtype=np.float32)
    for i, (y, x) in enumerate(positions):
        if i >= MAX_N:
            break
        feats[i, 0] = (x - cx) / max(W, 1)
        feats[i, 1] = (y - cy) / max(H, 1)
        feats[i, 2] = x / max(W - 1, 1)
        feats[i, 3] = 1.0 - feats[i, 2]
        feats[i, 4] = y / max(H - 1, 1)
        feats[i, 5] = 1.0 - feats[i, 4]
    return feats


# ── 增强 ─────────────────────────────────────────────────
def transform_point(y, x, H, W, k, flip):
    """旋转 k×90° + 水平翻转"""
    # 旋转
    if k == 1:
        y, x = x, H - 1 - y
        H, W = W, H
    elif k == 2:
        y, x = H - 1 - y, W - 1 - x
    elif k == 3:
        y, x = W - 1 - x, y
        H, W = W, H
    # 翻转
    if flip:
        x = W - 1 - x
    return y, x, H, W


def augment_and_encode(boxes, goals, H, W, n):
    """1 个样本 → 8 份 (input_vec, label_flat)"""
    results = []
    for k in range(4):
        for flip in [False, True]:
            # 变换坐标
            new_boxes, new_goals, new_H, new_W = [], [], H, W
            for y, x in boxes:
                ny, nx, new_H, new_W = transform_point(y, x, H, W, k, flip)
                new_boxes.append((ny, nx))
            for y, x in goals:
                ny, nx, _, _ = transform_point(y, x, H, W, k, flip)
                new_goals.append((ny, nx))

            # 重排序
            new_boxes.sort(key=lambda p: (p[0], p[1]))
            new_goals.sort(key=lambda p: (p[0], p[1]))

            # 算特征
            box_feat = compute_features(new_boxes, new_H, new_W)
            goal_feat = compute_features(new_goals, new_H, new_W)
            label = np.stack([box_feat, goal_feat], axis=0)  # [2, 5, 6]

            # 输入
            inp = np.array([new_W / MAX_SIZE, new_H / MAX_SIZE, n / MAX_N],
                           dtype=np.float32)

            results.append((inp, label))
    return results


# ── 主流程 ───────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser(description="布局特征数据解析")
    parser.add_argument('--max-per-size', type=int, default=3)
    parser.add_argument('--max-samples', type=int, default=0)
    parser.add_argument('--seed', type=int, default=42)
    args = parser.parse_args()

    np.random.seed(args.seed)
    os.makedirs(OUT_DIR, exist_ok=True)

    files = sorted(f for f in os.listdir(LEVEL_DIR)
                   if f.endswith('.txt') or f.endswith('.xsb'))

    print(f"布局特征模型 — 数据解析")
    print(f"  窗口: {MIN_SIZE}~{MAX_SIZE} × {MIN_SIZE}~{MAX_SIZE}")
    print(f"  Max N: {MAX_N}, 特征: 6 维 (vec_x/y + dist_l/r/t/b)")
    print(f"  筛选: box == goal >= 1")
    print(f"  每 (level,size) 采样: ≤{args.max_per_size}")
    print("-" * 60)

    X_list, y_list = [], []
    n_dist = {i: 0 for i in range(1, MAX_N + 1)}
    total_raw = 0

    for fname in files:
        fpath = os.path.join(LEVEL_DIR, fname)
        levels = parse_levels(fpath)
        n_before = len(X_list)

        for grid in levels:
            rect = rectify(grid)
            h, w = rect.shape

            for wh in range(MIN_SIZE, min(MAX_SIZE + 1, h + 1)):
                for ww in range(MIN_SIZE, min(MAX_SIZE + 1, w + 1)):
                    max_i = h - wh
                    max_j = w - ww
                    if max_i < 0 or max_j < 0:
                        continue

                    total_positions = (max_i + 1) * (max_j + 1)
                    n_pos = min(args.max_per_size, total_positions)

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
                        boxes, goals = extract_positions(window)
                        n_box, n_goal = len(boxes), len(goals)
                        if n_box == n_goal and 1 <= n_box <= MAX_N:
                            total_raw += 1
                            n_dist[n_box] += 1
                            for inp, label in augment_and_encode(
                                    boxes, goals, wh, ww, n_box):
                                X_list.append(inp)
                                y_list.append(label.ravel())  # [60]

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
            print(f"  ⚠ 已达样本上限，停止")
            break

    print("-" * 60)

    if len(X_list) == 0:
        print("❌ 无样本！")
        return

    # 打乱
    idx = np.random.permutation(len(X_list))
    X_arr = np.array(X_list, dtype=np.float32)[idx]   # [N, 3]
    y_arr = np.array(y_list, dtype=np.float32)[idx]   # [N, 60]
    del X_list, y_list

    print(f"\n原始窗口 (增强前): {total_raw:,}")
    print(f"增强后样本: {len(X_arr):,}")
    print(f"X shape: {X_arr.shape}")
    print(f"y shape: {y_arr.shape}")

    # N 分布
    n_all = (X_arr[:, 2] * MAX_N).round().astype(int)
    print("\nN 分布 (x8增强):")
    for n in range(1, MAX_N + 1):
        c = (n_all == n).sum()
        if c > 0:
            print(f"  n={n}: {c:>8d} ({c / len(X_arr) * 100:5.1f}%)")

    # 保存
    data_path = os.path.join(OUT_DIR, 'data.npz')
    np.savez_compressed(data_path, X=X_arr, y=y_arr)
    size_mb = os.path.getsize(data_path) / 1024 / 1024
    raw_mb = (X_arr.nbytes + y_arr.nbytes) / 1024 / 1024
    print(f"\n已保存 (原始 {raw_mb:.1f} MB, 压缩 {size_mb:.1f} MB): {data_path}")

    # 日记
    from datetime import datetime
    diary_path = os.path.join(OUT_DIR, 'diary.txt')
    with open(diary_path, 'w', encoding='utf-8') as f:
        f.write(f"生成时间: {datetime.now():%Y-%m-%d %H:%M:%S}\n")
        f.write(f"数据来源: 经典推箱子关卡 ({len(files)} 文件)\n")
        f.write(f"窗口: {MIN_SIZE}~{MAX_SIZE} × {MIN_SIZE}~{MAX_SIZE}\n")
        f.write(f"筛选: box == goal >= 1, max_n={MAX_N}\n")
        f.write(f"特征: 6维 (vec_x, vec_y, dist_l, dist_r, dist_t, dist_b)\n")
        f.write(f"增强: 旋转×4 × 镜像×2 = ×8\n")
        f.write(f"输入: [N, 3] (W/{MAX_SIZE}, H/{MAX_SIZE}, n/{MAX_N})\n")
        f.write(f"标签: [N, 60] (2×5×6 箱/目标各5位×6特征)\n")
        f.write(f"原始窗口: {total_raw:,}, 增强后: {len(X_arr):,}\n")
        f.write(f"文件: data.npz ({size_mb:.1f} MB)\n")
    print(f"  diary → {diary_path}")


if __name__ == '__main__':
    main()
