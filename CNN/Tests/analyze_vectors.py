"""
箱→目标 向量统计分析

从经典关卡提取每个箱-目标对的归一化 4D 向量:
  (box_y/H, box_x/W, goal_y/H, goal_x/W)

分析: KDE 密度、主方向聚类、尺寸分桶、与 W/H 的关系

用法:
  python Tests/analyze_vectors.py              # 全尺寸统计
  python Tests/analyze_vectors.py --w 10 --h 8 # 指定尺寸
"""

import os, sys, re, argparse
import numpy as np

CHAR_TO_ID = {'#':1,' ':2,'@':2,'.':3,'+':3,'$':4,'*':6}
_SCRIPT = os.path.dirname(os.path.abspath(__file__))
LEVEL_DIR = os.path.join(_SCRIPT, "../../Sokoban_Classic_Reference/3rdParty/Levels")
OUT_DIR = os.path.join(_SCRIPT, "../Tests/output")


def parse_levels(fp):
    with open(fp, 'r', encoding='utf-8') as f:
        content = f.read()
    blocks = re.split(r'\n\s*\n', content.strip())
    lvls = []
    for b in blocks:
        lines = [l.rstrip() for l in b.split('\n')
                 if not l.strip().startswith(';') and l.strip()]
        if not lines:
            continue
        grid = []
        for l in lines:
            row = [CHAR_TO_ID.get(c, 0) for c in l]
            if row:
                grid.append(row)
        if grid:
            lvls.append(grid)
    return lvls


def rectify(grid):
    h = len(grid)
    w = max(len(r) for r in grid)
    r = np.full((h, w), 2, dtype=np.int32)
    for i, row in enumerate(grid):
        r[i, :len(row)] = row
    return r


def extract_vectors(levels):
    """从所有关卡提取 (box_y/H, box_x/W, goal_y/H, goal_x/W, H, W, n_pairs)"""
    vectors = []
    for grid in levels:
        rect = rectify(grid)
        H, W = rect.shape

        # 找所有箱位置
        box_y, box_x = np.where((rect == 4) | (rect == 6))
        goal_y, goal_x = np.where((rect == 3) | (rect == 6))

        n_pairs = min(len(box_y), len(goal_y))
        if n_pairs == 0:
            continue

        # 贪心配对: 每箱找最近目标
        used_goal = set()
        for bi in range(min(len(box_y), n_pairs)):
            by, bx_ = box_y[bi], box_x[bi]
            # 找最近未配目标
            best_d = 1e9
            best_g = -1
            for gi in range(len(goal_y)):
                if gi in used_goal:
                    continue
                d = abs(box_y[bi] - goal_y[gi]) + abs(box_x[bi] - goal_x[gi])
                if d < best_d:
                    best_d = d
                    best_g = gi
            if best_g >= 0:
                used_goal.add(best_g)
                gy, gx = goal_y[best_g], goal_x[best_g]
                vectors.append((
                    by / H, bx_ / W,   # box 归一化位置
                    gy / H, gx / W,    # goal 归一化位置
                    H, W, n_pairs,
                ))

    return vectors


def compute_delta(vectors):
    """从 4D 向量提取 delta = goal - box"""
    return np.array([
        [v[2] - v[0], v[3] - v[1]]  # (dy, dx)
        for v in vectors
    ])


def print_kde_stats(vectors, title="全尺寸"):
    """打印统计摘要"""
    arr = np.array(vectors)
    if len(arr) == 0:
        print("  (无数据)")
        return

    box_y, box_x = arr[:, 0], arr[:, 1]
    goal_y, goal_x = arr[:, 2], arr[:, 3]
    dy = goal_y - box_y
    dx = goal_x - box_x
    dist = np.sqrt(dy**2 + dx**2)

    print(f"\n{'='*60}")
    print(f"  {title}  |  N={len(arr):,}")
    print(f"{'='*60}")
    print(f"  Box  位置:  y={box_y.mean():.3f}±{box_y.std():.3f}"
          f"  x={box_x.mean():.3f}±{box_x.std():.3f}")
    print(f"  Goal 位置:  y={goal_y.mean():.3f}±{goal_y.std():.3f}"
          f"  x={goal_x.mean():.3f}±{goal_x.std():.3f}")
    print(f"  Delta (g-b): dy={dy.mean():.3f}±{dy.std():.3f}"
          f"  dx={dx.mean():.3f}±{dx.std():.3f}")
    print(f"  |Delta|:       {dist.mean():.3f}±{dist.std():.3f}"
          f"  min={dist.min():.3f}  max={dist.max():.3f}")

    # 方向象限分布
    quadrants = {}
    for d in zip(dx, dy):
        qx = 'L' if d[0] < -0.01 else ('R' if d[0] > 0.01 else 'C')
        qy = 'T' if d[1] < -0.01 else ('B' if d[1] > 0.01 else 'C')
        key = qy + qx
        quadrants[key] = quadrants.get(key, 0) + 1
    print(f"  方向分布:")
    for k in sorted(quadrants, key=lambda x: -quadrants[x]):
        print(f"    {k}: {quadrants[k]/len(arr)*100:5.1f}% ({quadrants[k]})")


def bucket_by_size(vectors, size_step=5):
    """按 (H, W) 分桶统计"""
    buckets = {}
    for v in vectors:
        h_bin = (v[4] - 1) // size_step * size_step + 1
        w_bin = (v[5] - 1) // size_step * size_step + 1
        key = (h_bin, w_bin)
        if key not in buckets:
            buckets[key] = []
        buckets[key].append(v)
    return buckets


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--w', type=int, default=0, help='筛选指定宽度')
    p.add_argument('--h', type=int, default=0, help='筛选指定高度')
    p.add_argument('--kde', action='store_true', help='尝试 KDE 可视化')
    p.add_argument('--save-kde', action='store_true', help='保存 KDE 模型为文件')
    a = p.parse_args()

    os.makedirs(OUT_DIR, exist_ok=True)

    files = sorted(f for f in os.listdir(LEVEL_DIR)
                   if f.endswith('.txt') or f.endswith('.xsb'))
    print(f"解析 {len(files)} 个关卡文件...")

    all_vectors = []
    for fn in files:
        lvls = parse_levels(os.path.join(LEVEL_DIR, fn))
        vecs = extract_vectors(lvls)
        all_vectors.extend(vecs)

    print(f"总箱-目标对: {len(all_vectors):,}")

    if a.h > 0 and a.w > 0:
        # 过滤指定尺寸
        filtered = [v for v in all_vectors
                    if v[4] == a.h and v[5] == a.w]
        print(f"  过滤 {a.h}×{a.w}: {len(filtered)} 对")
        print_kde_stats(filtered, f"尺寸={a.h}×{a.w}")

        if a.kde and len(filtered) >= 20:
            save_kde_2d(filtered, a.h, a.w)
    else:
        print_kde_stats(all_vectors)

        # 分桶统计
        buckets = bucket_by_size(all_vectors)
        print(f"\n{'='*60}")
        print(f"  尺寸分桶 (步长=5)")
        print(f"{'='*60}")
        for key in sorted(buckets.keys()):
            v = buckets[key]
            if len(v) < 10:
                continue
            arr = np.array(v)
            dy = arr[:, 2] - arr[:, 0]
            dx = arr[:, 3] - arr[:, 1]
            dist = np.sqrt(dy**2 + dx**2)
            print(f"  ({key[0]:>2d},{key[1]:>2d}): N={len(v):>4d}"
                  f"  |delta|={dist.mean():.3f}"
                  f"  box_y,x=({arr[:,0].mean():.3f},{arr[:,1].mean():.3f})"
                  f"  goal_y,x=({arr[:,2].mean():.3f},{arr[:,3].mean():.3f})")

        # 保存完整 KDE 数据
        if a.save_kde:
            save_kde_4d(all_vectors)

    # ── 关键发现 ──
    print(f"\n{'='*60}")
    print(f"  结论")
    print(f"{'='*60}")

    arr = np.array(all_vectors)
    dy_all = arr[:, 2] - arr[:, 0]
    dx_all = arr[:, 3] - arr[:, 1]

    # 箱和目标的距离分布
    dist = np.sqrt(dy_all**2 + dx_all**2)
    print(f"  |Δ| 分布: P10={np.percentile(dist,10):.3f}"
          f"  P50={np.percentile(dist,50):.3f}"
          f"  P90={np.percentile(dist,90):.3f}")

    # 主方向模态
    angles = np.arctan2(dy_all, dx_all)
    angle_bins = np.linspace(-np.pi, np.pi, 9)
    hist, _ = np.histogram(angles, bins=angle_bins)
    print(f"  方向角度分布 (8等分, -π~π):")
    labels = ['E', 'NE/SE', 'N', 'NW/SW', 'W', 'SW/NW', 'S', 'SE/NE']
    for i, (lbl, cnt) in enumerate(zip(labels, hist)):
        bar = '█' * int(cnt / hist.max() * 30)
        print(f"    {lbl:>6s}: {cnt:>6d} ({cnt/len(angles)*100:5.1f}%) {bar}")


if __name__ == '__main__':
    main()
