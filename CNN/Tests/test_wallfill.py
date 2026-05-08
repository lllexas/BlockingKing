"""WallFill 单独测试：真实布局 vs 随机布局"""
import sys, os, argparse, time
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
from model_loader import load_wall_fill
from inference import relu, sigmoid, conv2d_pad1, conv2d_1x1

MAX_N = 5
_SCRIPT = os.path.dirname(os.path.abspath(__file__))

def infer_wall_fill(model, box_map, goal_map, n):
    H, W = box_map.shape
    x = np.zeros((4, H, W), dtype=np.float32)
    x[0] = box_map; x[1] = goal_map; x[2] = n / MAX_N; x[3] = 1.0
    x = relu(conv2d_pad1(x, model['conv1_w'], model['conv1_b']))
    x = relu(conv2d_pad1(x, model['conv2_w'], model['conv2_b']))
    x = relu(conv2d_pad1(x, model['conv3_w'], model['conv3_b']))
    x = relu(conv2d_pad1(x, model['conv4_w'], model['conv4_b']))
    x = relu(conv2d_pad1(x, model['conv5_w'], model['conv5_b']))
    x = conv2d_1x1(x, model['conv6_w'], model['conv6_b'])
    return sigmoid(x[0])

def ascii_full(box_map, goal_map, wall_map):
    H, W = wall_map.shape
    chars = {0: ' ', 1: '#', 2: '$', 3: '.', 4: '*'}
    lines = []
    for y in range(H):
        row = []
        for x in range(W):
            v = 1 if wall_map[y, x] else 0
            if box_map[y, x] and goal_map[y, x]: v = 4
            elif box_map[y, x]: v = 2
            elif goal_map[y, x]: v = 3
            row.append(chars[v])
        lines.append(''.join(row))
    return '\n'.join(lines)

def load_real_samples(n_samples=5):
    """从 WallFill 验证集取真实样本"""
    dp = os.path.join(_SCRIPT, "../MapWallFill/data/data.npz")
    if not os.path.exists(dp):
        print(f"⚠ 找不到 {dp}"); return []
    d = np.load(dp)
    X, y = d['X'], d['y']  # X:[N,4,20,20] y:[N,1,20,20]
    # 取验证部分 (后10%)
    nv = int(len(X) * 0.1)
    idx = np.arange(len(X))[-nv:]
    np.random.shuffle(idx)
    samples = []
    for i in idx[:n_samples]:
        mask = X[i, 3] > 0.5
        ys, xs = np.where(mask)
        if len(ys) == 0: continue
        h, w = ys.max() - ys.min() + 1, xs.max() - xs.min() + 1
        box_map = X[i, 0, :h, :w]
        goal_map = X[i, 1, :h, :w]
        n = int(round(box_map.sum()))
        wall_true = y[i, 0, :h, :w]
        samples.append({
            'box': box_map, 'goal': goal_map, 'wall_true': wall_true,
            'n': n, 'h': h, 'w': w,
        })
    return samples

def make_random_sample(W, H, n):
    """生成随机箱/目标布局"""
    cells = [(y, x) for y in range(H) for x in range(W)]
    chosen = np.random.choice(len(cells), size=n * 2, replace=False)
    box_map = np.zeros((H, W), dtype=np.float32)
    goal_map = np.zeros((H, W), dtype=np.float32)
    for k in range(n):
        y, x = cells[chosen[k]]
        box_map[y, x] = 1.0
    for k in range(n, n * 2):
        y, x = cells[chosen[k]]
        goal_map[y, x] = 1.0
    return box_map, goal_map

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--wall-fill', default=f"{_SCRIPT}/../MapWallFill/weights/epoch30/")
    p.add_argument('--n', type=int, default=5, help='样本数')
    a = p.parse_args()

    model = load_wall_fill(a.wall_fill)

    # ── 真实 Sokoban 布局 ──
    print("=" * 60)
    print("A) 真实 Sokoban 箱/目标 → WallFill")
    print("=" * 60)
    samples = load_real_samples(a.n)
    for i, s in enumerate(samples):
        wall_pred = infer_wall_fill(model, s['box'], s['goal'], s['n']) > 0.5
        wp = wall_pred.sum() / (s['h'] * s['w']) * 100
        wt = s['wall_true'].sum() / (s['h'] * s['w']) * 100
        print(f"\n── 样本 {i+1} ({s['h']}×{s['w']}, n={s['n']}) ──")
        print(f"  真实墙: {wt:.0f}%  |  预测墙: {wp:.0f}%")
        print(ascii_full(s['box'], s['goal'], wall_pred))

    # ── 随机布局 ──
    print(f"\n{'=' * 60}")
    print("B) 随机洒箱/目标 → WallFill")
    print("=" * 60)
    for i in range(a.n):
        W, H = np.random.randint(8, 16), np.random.randint(6, 12)
        n = np.random.randint(1, 4)
        box_map, goal_map = make_random_sample(W, H, n)
        wall_pred = infer_wall_fill(model, box_map, goal_map, n) > 0.5
        wp = wall_pred.sum() / (W * H) * 100
        print(f"\n── 随机 {i+1} ({H}×{W}, n={n}) ──")
        print(f"  预测墙: {wp:.0f}%")
        print(ascii_full(box_map, goal_map, wall_pred))


if __name__ == '__main__':
    main()
