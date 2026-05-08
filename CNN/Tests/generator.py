"""
地图生成器 — 三段管线

  BoxPlacer  (W,H,n)  → 箱热力图 → 采样 → 箱位置
  GoalPlacer 箱图     → 目标热力图 → 采样 → 目标位置
  WallFill   箱图+目标图 → 墙图
"""

import numpy as np
from model_loader import load_box_placer, load_goal_placer, load_wall_fill
from inference import relu, sigmoid, conv2d_pad1, conv2d_1x1

MAX_N = 5


class Generator:
    def __init__(self, box_placer_dir, goal_placer_dir, wall_fill_dir):
        self.bp = load_box_placer(box_placer_dir)
        self.gp = load_goal_placer(goal_placer_dir)
        self.wf = load_wall_fill(wall_fill_dir)

    def generate(self, W, H, n, batch=1, seed=None, temperature=1.0):
        """生成 batch 张地图"""
        if seed is not None:
            np.random.seed(seed)

        results = []
        for _ in range(batch):
            # 1. BoxPlacer → 箱热力图 → 采样
            box_heat = self._infer_box_placer(W, H, n)
            box_pos = sample_from_heatmap(box_heat, n, temperature)
            box_map = positions_to_map(box_pos, H, W)

            # 2. GoalPlacer → 目标热力图 → 采样 (避开箱位)
            goal_heat = self._infer_goal_placer(box_map)
            goal_pos = sample_from_heatmap(goal_heat, n, temperature, exclude=box_pos)
            goal_map = positions_to_map(goal_pos, H, W)

            # 3. WallFill → 墙
            wall_map = self._infer_wall_fill(box_map, goal_map, n)

            full = make_full_map(wall_map, box_map, goal_map)
            results.append({
                'wall': wall_map, 'box': box_map, 'goal': goal_map,
                'full': full, 'n': n,
                'box_count': len(box_pos), 'goal_count': len(goal_pos),
            })
        return results

    # ── 三段推理 ────────────────────────────────────────
    def _infer_conv_heatmap(self, model, x):
        """通用 Conv 推理: x [C,H,W] → heatmap [H,W]"""
        x = relu(conv2d_pad1(x, model['conv1_w'], model['conv1_b']))
        x = relu(conv2d_pad1(x, model['conv2_w'], model['conv2_b']))
        x = relu(conv2d_pad1(x, model['conv3_w'], model['conv3_b']))
        x = relu(conv2d_pad1(x, model['conv4_w'], model['conv4_b']))
        x = relu(conv2d_pad1(x, model['conv5_w'], model['conv5_b']))
        x = conv2d_1x1(x, model['conv6_w'], model['conv6_b'])
        return sigmoid(x[0])

    def _infer_box_placer(self, W, H, n):
        x = np.zeros((2, H, W), dtype=np.float32)
        x[0] = n / MAX_N       # 常数 n/5
        x[1] = 1.0              # valid mask
        return self._infer_conv_heatmap(self.bp, x)

    def _infer_goal_placer(self, box_map):
        H, W = box_map.shape
        x = np.zeros((2, H, W), dtype=np.float32)
        x[0] = box_map
        x[1] = 1.0
        return self._infer_conv_heatmap(self.gp, x)

    def _infer_wall_fill(self, box_map, goal_map, n):
        H, W = box_map.shape
        x = np.zeros((4, H, W), dtype=np.float32)
        x[0] = box_map
        x[1] = goal_map
        x[2] = n / MAX_N
        x[3] = 1.0
        x = relu(conv2d_pad1(x, self.wf['conv1_w'], self.wf['conv1_b']))
        x = relu(conv2d_pad1(x, self.wf['conv2_w'], self.wf['conv2_b']))
        x = relu(conv2d_pad1(x, self.wf['conv3_w'], self.wf['conv3_b']))
        x = relu(conv2d_pad1(x, self.wf['conv4_w'], self.wf['conv4_b']))
        x = relu(conv2d_pad1(x, self.wf['conv5_w'], self.wf['conv5_b']))
        x = conv2d_1x1(x, self.wf['conv6_w'], self.wf['conv6_b'])
        return sigmoid(x[0]) > 0.5


# ── 概率采样 ──────────────────────────────────────────────
def sample_from_heatmap(heatmap, n, temperature=1.0, exclude=None):
    """从热力图中采样 n 个不重复位置
    temperature < 1.0 → 更贪心 (取高峰), > 1.0 → 更随机
    exclude: set of (y,x) 不可选的位置
    """
    H, W = heatmap.shape
    probs = heatmap.copy()

    # 屏蔽不可选位置
    if exclude:
        for y, x in exclude:
            probs[y, x] = 0.0

    # 温度调节
    if temperature != 1.0:
        probs = np.power(probs, 1.0 / max(temperature, 0.01))

    # 归一化
    total = probs.sum()
    if total < 1e-8:
        probs = np.ones((H, W), dtype=np.float32) / (H * W)
        if exclude:
            for y, x in exclude:
                probs[y, x] = 0.0
            total = probs.sum()
            probs /= total
    else:
        probs /= total

    flat = probs.ravel()
    indices = np.random.choice(H * W, size=n, replace=False, p=flat)
    return [(idx // W, idx % W) for idx in indices]


def positions_to_map(positions, H, W):
    m = np.zeros((H, W), dtype=np.float32)
    for y, x in positions:
        m[y, x] = 1.0
    return m


# ── 合成 ──────────────────────────────────────────────────
def make_full_map(wall_map, box_map, goal_map):
    """0=地板 1=墙 2=箱 3=目标 4=箱在目标"""
    H, W = wall_map.shape
    full = np.full((H, W), 0, dtype=np.int32)
    full[wall_map] = 1
    for y in range(H):
        for x in range(W):
            if box_map[y, x] and goal_map[y, x]:
                full[y, x] = 4
            elif box_map[y, x]:
                full[y, x] = 2
            elif goal_map[y, x]:
                full[y, x] = 3
    return full


# ── ASCII ─────────────────────────────────────────────────
TILE_CHAR = {0: ' ', 1: '#', 2: '$', 3: '.', 4: '*'}


def ascii_map(full_map):
    return '\n'.join(''.join(TILE_CHAR.get(c, '?') for c in row) for row in full_map)
