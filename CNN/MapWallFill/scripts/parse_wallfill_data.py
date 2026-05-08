"""
墙壁填充模型 — 数据解析脚本
从经典推箱子关卡提取 (箱位置, 目标位置, n) → 墙二值图 的训练样本。

输入: [4, 20, 20] (箱图 + 目标图 + n/5 + valid_mask)
标签: [1, 20, 20] (墙二值图)

用法: python parse_wallfill_data.py [--max-per-size N]
"""

import os, sys, re, argparse
import numpy as np

MIN_SIZE   = 5
MAX_SIZE   = 20
MAX_N      = 5
CHAR_TO_ID = {
    '#': 1, ' ': 2, '@': 2, '.': 3, '+': 3, '$': 4, '*': 6,
}

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
LEVEL_DIR   = os.path.join(_SCRIPT_DIR, "../../../Sokoban_Classic_Reference/3rdParty/Levels")
OUT_DIR     = os.path.join(_SCRIPT_DIR, "../data")


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


def make_sample(window, n):
    """(H,W) 窗口 → (X_4ch, y_1ch), 未 pad"""
    h, w = window.shape
    X = np.zeros((4, h, w), dtype=np.float32)
    X[0] = ((window == 4) | (window == 6)).astype(np.float32)   # 箱
    X[1] = ((window == 3) | (window == 6)).astype(np.float32)   # 目标
    X[2] = n / MAX_N
    X[3] = 1.0                                                   # valid_mask

    y = np.zeros((1, h, w), dtype=np.float32)
    y[0] = (window == 1).astype(np.float32)                      # 墙

    return X, y


def augment_and_pad(X, y):
    """1 个样本 → 8 份增强 + pad 到 20×20"""
    results = []
    for k in range(4):
        X_rot = np.rot90(X, k=k, axes=(1, 2))
        y_rot = np.rot90(y, k=k, axes=(1, 2))
        for flip in [False, True]:
            X_var = np.flip(X_rot, axis=2) if flip else X_rot
            y_var = np.flip(y_rot, axis=2) if flip else y_rot
            _, h, w = X_var.shape
            X_pad = np.zeros((4, MAX_SIZE, MAX_SIZE), dtype=np.float32)
            y_pad = np.zeros((1, MAX_SIZE, MAX_SIZE), dtype=np.float32)
            X_pad[:, :h, :w] = X_var
            y_pad[:, :h, :w] = y_var
            results.append((X_pad, y_pad))
    return results


def main():
    parser = argparse.ArgumentParser(description="墙壁填充数据解析")
    parser.add_argument('--max-per-size', type=int, default=3)
    parser.add_argument('--max-samples', type=int, default=0)
    parser.add_argument('--seed', type=int, default=42)
    args = parser.parse_args()

    np.random.seed(args.seed)
    os.makedirs(OUT_DIR, exist_ok=True)

    files = sorted(f for f in os.listdir(LEVEL_DIR)
                   if f.endswith('.txt') or f.endswith('.xsb'))

    print(f"墙壁填充模型 — 数据解析")
    print(f"  窗口: {MIN_SIZE}~{MAX_SIZE}, 筛选: box==goal>=1")
    print(f"  输入: 4ch (箱+目标+n/5+mask), 标签: 1ch (墙)")
    print("-" * 60)

    X_list, y_list = [], []
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

                    n_pos = min(args.max_per_size, (max_i + 1) * (max_j + 1))
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
                        n_box = ((window == 4) | (window == 6)).sum()
                        n_goal = ((window == 3) | (window == 6)).sum()
                        if n_box == n_goal and 1 <= n_box <= MAX_N:
                            total_raw += 1
                            X_raw, y_raw = make_sample(window, n_box)
                            for X_pad, y_pad in augment_and_pad(X_raw, y_raw):
                                X_list.append(X_pad)
                                y_list.append(y_pad)

                            if args.max_samples > 0 and len(X_list) >= args.max_samples:
                                break
                    if args.max_samples > 0 and len(X_list) >= args.max_samples:
                        break
                if args.max_samples > 0 and len(X_list) >= args.max_samples:
                    break
            if args.max_samples > 0 and len(X_list) >= args.max_samples:
                break

        n_file = len(X_list) - n_before
        print(f"  {fname:<25s} → {len(levels):>4d} 关, {n_file:>8d} 增强 "
              f"(累计 {len(X_list):,})", flush=True)

        if args.max_samples > 0 and len(X_list) >= args.max_samples:
            print("  ⚠ 达上限")
            break

    print("-" * 60)

    if not X_list:
        print("❌ 无样本")
        return

    idx = np.random.permutation(len(X_list))
    X_arr = np.array(X_list, dtype=np.float32)[idx]
    y_arr = np.array(y_list, dtype=np.float32)[idx]
    del X_list, y_list

    # 墙比例
    masks = X_arr[:, 3] > 0.5
    wall_ratio = (y_arr[:, 0] * masks).sum() / masks.sum()
    print(f"\n原始: {total_raw:,},  增强: {len(X_arr):,}")
    print(f"X: {X_arr.shape}, y: {y_arr.shape}")
    print(f"墙占有效格: {wall_ratio:.1%}")

    data_path = os.path.join(OUT_DIR, 'data.npz')
    np.savez_compressed(data_path, X=X_arr, y=y_arr)
    size_mb = os.path.getsize(data_path) / 1024 / 1024
    print(f"已保存: {data_path} ({size_mb:.1f} MB)")

    from datetime import datetime
    diary_path = os.path.join(OUT_DIR, 'diary.txt')
    with open(diary_path, 'w', encoding='utf-8') as f:
        f.write(f"生成: {datetime.now():%Y-%m-%d %H:%M:%S}\n")
        f.write(f"输入: [N,4,20,20] (箱+目标+n/5+mask)\n")
        f.write(f"标签: [N,1,20,20] (墙)\n")
        f.write(f"墙比例: {wall_ratio:.1%}\n")
        f.write(f"原始: {total_raw:,}, 增强: {len(X_arr):,}\n")
        f.write(f"大小: {size_mb:.1f} MB\n")
    print(f"  diary → {diary_path}")


if __name__ == '__main__':
    main()
