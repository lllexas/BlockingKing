"""
规则引擎 v3: 平行线穿房法

1. Roll 方向(dy,dx)
2. 沿垂直轴均分 n 条平行线
3. 每条线 → 取与房间的交线段 → 两端各放箱和目标
4. 箱靠边缘(2~3格), 目标靠内侧(距箱1~2格)
"""

import numpy as np
from collections import defaultdict

# 方向: (dy, dx)
DIRS = {'H': (0, 1), 'V': (1, 0), 'D1': (1, 1), 'D2': (1, -1)}


def generate(W, H, n, direction=None, seed=None, box_edge=(2, 3),
             goal_dist=(1, 2), perturb=1):
    rng = np.random.RandomState(seed)
    if direction is None:
        direction = rng.choice(list(DIRS.keys()))
    dy, dx = DIRS[direction]

    # 垂直轴: 投影值 = y * 垂直_y + x * 垂直_x
    # 行进方向 (dy,dx) 的垂直方向是 (dx, -dy)
    py, px = dx, -dy

    # 所有格按垂直轴投影值分组 = 平行线
    bands = defaultdict(list)
    for y in range(H):
        for x in range(W):
            proj = y * py + x * px
            bands[proj].append((y, x))

    # 取有效的投影值 (该线上至少3格才能放箱+目标)
    valid = sorted(k for k, v in bands.items() if len(v) >= 3)
    if len(valid) < n:
        return [], [], direction

    # 等距取 n 条线
    if n == 1:
        chosen = [valid[len(valid) // 2]]
    else:
        idx = np.linspace(1, len(valid) - 2, n).astype(int)
        chosen = [valid[i] for i in idx]

    boxes, goals = [], []
    used = set()

    for cp in chosen:
        line = bands[cp]
        # 沿行进方向排序
        line.sort(key=lambda c: c[0] * dy + c[1] * dx)
        # 线段两端: 一端放箱, 另一端放目标
        # 随机选箱放哪端
        if rng.choice([True, False]):
            box_end, goal_end = 0, -1  # 箱在前端, 目标在后端
        else:
            box_end, goal_end = -1, 0  # 箱在后端, 目标在前端

        d_box = rng.randint(box_edge[0], box_edge[1] + 1)
        d_goal = rng.randint(goal_dist[0], goal_dist[1] + 1)

        if box_end == 0:
            bi = min(d_box, len(line) - 1)
            gi = min(bi + d_goal, len(line) - 1)
        else:
            bi = max(len(line) - 1 - d_box, 0)
            gi = max(bi - d_goal, 0)

        by, bx = _jitter(line[bi], W, H, perturb, rng, used)
        gy, gx = _jitter(line[gi], W, H, perturb, rng, used)
        used.add((by, bx)); used.add((gy, gx))
        boxes.append((by, bx)); goals.append((gy, gx))

    return boxes, goals, direction


def _jitter(cell, W, H, d, rng, used):
    y, x = cell
    for _ in range(30):
        ny = np.clip(y + rng.randint(-d, d + 1), 0, H - 1)
        nx = np.clip(x + rng.randint(-d, d + 1), 0, W - 1)
        if (ny, nx) not in used:
            return ny, nx
    return y, x


# ── 可视化 ──────────────────────────────────────────────
def ascii_map(W, H, boxes, goals):
    g = np.full((H, W), ' ', dtype=str)
    for y, x in goals: g[y, x] = '.'
    for y, x in boxes: g[y, x] = '*' if g[y, x] == '.' else '$'
    return '\n'.join(''.join(r) for r in g)


if __name__ == '__main__':
    for direction in DIRS:
        for i in range(3):
            boxes, goals, d = generate(14, 10, n=3, direction=direction, seed=i * 10)
            print(f"\n── {d} (seed={i*10}) ──")
            print(ascii_map(14, 10, boxes, goals))
