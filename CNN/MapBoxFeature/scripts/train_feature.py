"""
布局特征模型 — 训练脚本
输入: [3] (W/20, H/20, n/5)
输出: [60] (2×5×6 — 箱/目标各5位置×6特征)
FC 网络, masked MSE
"""

import os, sys, argparse, copy
import numpy as np
import torch
import torch.nn as nn
from torch.utils.data import TensorDataset, DataLoader

# ── 超参数 ────────────────────────────────────────────────
BATCH_SIZE     = 512
LR             = 0.001
EPOCHS         = 30
VAL_SPLIT      = 0.1
RANDOM_SEED    = 42
MAX_N          = 5
MAX_SIZE       = 20
SAVE_EPOCHS    = [1, 2, 3, 5, 10, 20, 30]
PATIENCE       = 8            # 提前跳出
FEAT_DIM       = 6     # 每位置特征数

_SCRIPT_DIR    = os.path.dirname(os.path.abspath(__file__))
DATA_PATH      = os.path.join(_SCRIPT_DIR, "../data/data.npz")
WEIGHT_DIR     = os.path.join(_SCRIPT_DIR, "../weights")


# ── 模型 ──────────────────────────────────────────────────
class BoxFeatureNet(nn.Module):
    def __init__(self):
        super().__init__()
        self.fc1 = nn.Linear(3, 16)
        self.fc2 = nn.Linear(16, 32)
        self.fc3 = nn.Linear(32, 32)
        self.fc4 = nn.Linear(32, MAX_N * 2 * FEAT_DIM)  # 60

    def forward(self, x):
        x = torch.relu(self.fc1(x))
        x = torch.relu(self.fc2(x))
        x = torch.relu(self.fc3(x))
        return self.fc4(x)


# ── 损失 ──────────────────────────────────────────────────
def masked_mse(pred, target, n_values):
    """仅在前 n 个活跃条目上计算 MSE
    pred/target: [B, 60]  (flattened 2×5×6)
    """
    B = pred.size(0)
    mask = torch.zeros_like(pred)
    for i in range(B):
        n = int(n_values[i].item())
        n_feats = n * FEAT_DIM  # 活跃特征数
        mask[i, :n_feats] = 1.0            # 箱的前 n 个
        mask[i, 30:30 + n_feats] = 1.0     # 目标的前 n 个
    mse = (pred - target) ** 2 * mask
    return mse.sum() / (mask.sum() + 1e-8)


def feature_distance(pred, target, n_values):
    """计算箱/目标预测位置与真实位置的欧氏距离，仅在活跃条目上"""
    pred = pred.detach().cpu().numpy()
    target = target.detach().cpu().numpy()
    n_vals = n_values.detach().cpu().numpy().astype(int)

    dists_box, dists_goal = [], []
    for i in range(len(pred)):
        n = n_vals[i]
        p = pred[i].reshape(2, MAX_N, FEAT_DIM)
        t = target[i].reshape(2, MAX_N, FEAT_DIM)

        # 用 vec_x (idx 0) 和 vec_y (idx 1) 算距离
        for ch in [0, 1]:  # 0=箱, 1=目标
            for k in range(n):
                dx = p[ch, k, 0] - t[ch, k, 0]
                dy = p[ch, k, 1] - t[ch, k, 1]
                d = np.sqrt(dx * dx + dy * dy)
                if ch == 0:
                    dists_box.append(d)
                else:
                    dists_goal.append(d)

    return (np.mean(dists_box) if dists_box else 0,
            np.mean(dists_goal) if dists_goal else 0)


# ── 权重导出 ──────────────────────────────────────────────
def export_weights(model, out_dir, epoch, metrics):
    """FC 层权重导出，适配 Unity C#: W [in,out] 存为二进制"""
    os.makedirs(out_dir, exist_ok=True)
    state = model.state_dict()

    for i in range(1, 5):
        w = state[f"fc{i}.weight"].cpu().numpy()  # [out, in]
        b = state[f"fc{i}.bias"].cpu().numpy()    # [out]
        w.T.tofile(f"{out_dir}/fc{i}_weight.bin")  # [in, out]
        b.tofile(f"{out_dir}/fc{i}_bias.bin")

    from datetime import datetime
    n_params = sum(p.numel() for p in model.parameters())
    with open(f"{out_dir}/diary.txt", 'w', encoding='utf-8') as f:
        f.write(f"生成时间: {datetime.now():%Y-%m-%d %H:%M:%S}\n")
        f.write(f"Epoch: {epoch}\n")
        f.write(f"架构: FC(3→16→32→32→60)\n")
        f.write(f"参数: {n_params}\n")
        f.write(f"BATCH: {BATCH_SIZE}, LR: {LR}\n")
        for k, v in metrics.items():
            f.write(f"{k}: {v:.6f}\n")


# ── 训练 ──────────────────────────────────────────────────
def train(args):
    torch.manual_seed(RANDOM_SEED)
    np.random.seed(RANDOM_SEED)

    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f"设备: {device}", flush=True)

    # ── 加载 ─────────────────────────────────────────
    print("加载数据...", flush=True)
    data = np.load(DATA_PATH)
    X, y = data['X'], data['y']  # [N, 3], [N, 60]
    print(f"  X: {X.shape}, y: {y.shape}", flush=True)

    n_val = int(len(X) * VAL_SPLIT)
    idx = np.random.permutation(len(X))
    X_train, X_val = X[idx[:-n_val]], X[idx[-n_val:]]
    y_train, y_val = y[idx[:-n_val]], y[idx[-n_val:]]

    print(f"  训练: {len(X_train):,},  验证: {len(X_val):,}", flush=True)

    X_train_t = torch.from_numpy(X_train).float()
    y_train_t = torch.from_numpy(y_train).float()
    X_val_t   = torch.from_numpy(X_val).float()
    y_val_t   = torch.from_numpy(y_val).float()

    train_dl = DataLoader(TensorDataset(X_train_t, y_train_t),
                          batch_size=BATCH_SIZE, shuffle=True, pin_memory=True)
    val_dl   = DataLoader(TensorDataset(X_val_t, y_val_t),
                          batch_size=BATCH_SIZE * 2, pin_memory=True)

    # ── 模型 ─────────────────────────────────────────
    model = BoxFeatureNet().to(device)
    n_params = sum(p.numel() for p in model.parameters())
    print(f"  参数: {n_params}", flush=True)

    optimizer = torch.optim.Adam(model.parameters(), lr=LR)

    print(f"\n训练 {EPOCHS} 轮, 保存: epoch {SAVE_EPOCHS}, patience={PATIENCE}", flush=True)
    print("-" * 75, flush=True)
    header = (f"{'Ep':>4s}  {'Train Loss':>10s}  {'Val Loss':>8s}  "
              f"{'Box Dist':>8s}  {'Goal Dist':>8s}  {'Stale':>5s}")
    print(header, flush=True)
    print("-" * 75, flush=True)

    best_val_loss = float('inf')
    best_state = None
    stale_count = 0
    stopped_early = False

    for epoch in range(1, EPOCHS + 1):
        # ── train ──
        model.train()
        train_loss = 0.0
        for bx, by in train_dl:
            bx, by = bx.to(device, non_blocking=True), by.to(device, non_blocking=True)
            n_vals = (bx[:, 2] * MAX_N).round().long()
            optimizer.zero_grad()
            pred = model(bx)
            loss = masked_mse(pred, by, n_vals)
            loss.backward()
            optimizer.step()
            train_loss += loss.item() * bx.size(0)

        train_loss /= len(X_train)

        # ── val ──
        model.eval()
        val_loss = 0.0
        all_preds, all_targets, all_n = [], [], []
        with torch.no_grad():
            for bx, by in val_dl:
                bx, by = bx.to(device), by.to(device)
                n_vals = (bx[:, 2] * MAX_N).round().long()
                pred = model(bx)
                val_loss += masked_mse(pred, by, n_vals).item() * bx.size(0)
                all_preds.append(pred)
                all_targets.append(by)
                all_n.append(n_vals)

        val_loss /= len(X_val)
        preds = torch.cat(all_preds)
        targets = torch.cat(all_targets)
        ns = torch.cat(all_n)

        box_dist, goal_dist = feature_distance(preds, targets, ns)

        # ── 提前跳出 ──
        improved = val_loss < best_val_loss - 1e-6
        if improved:
            best_val_loss = val_loss
            best_state = copy.deepcopy(model.state_dict())
            stale_count = 0
        else:
            stale_count += 1

        print(f"{epoch:>4d}  {train_loss:>10.6f}  "
              f"{val_loss:>8.6f}  {box_dist:>7.4f}  {goal_dist:>7.4f}  "
              f"{stale_count:>4d}{' *' if improved else ''}",
              flush=True)

        # ── save ──
        if epoch in SAVE_EPOCHS:
            ckpt_dir = f"{WEIGHT_DIR}/epoch{epoch:02d}"
            metrics = {'val_loss': val_loss, 'box_dist': box_dist,
                       'goal_dist': goal_dist}
            export_weights(model, ckpt_dir, epoch, metrics)
            print(f"  → 已保存 epoch {epoch}", flush=True)

        if stale_count >= PATIENCE:
            print(f"\n  Early stop at epoch {epoch} (best val_loss={best_val_loss:.6f})",
                  flush=True)
            stopped_early = True
            break

    print("-" * 75, flush=True)

    if stopped_early and best_state is not None:
        model.load_state_dict(best_state)

    print(f"\n最终验证指标 (best epoch):")
    print(f"  Best Val Loss: {best_val_loss:.6f}")
    print(f"  Box Dist (vec): {box_dist:.4f}")
    print(f"  Goal Dist (vec): {goal_dist:.4f}")
    if stopped_early:
        print(f"  Early stop: 是 (patience={PATIENCE})")

    # 随机基线 (均匀随机位置的向量距离期望)
    # 对单位正方形均匀分布，两点期望距离 ≈ 0.52
    print(f"\n  (随机基线 dist ≈ 0.52)")

    print(f"\n权重输出目录:", flush=True)
    for ep in SAVE_EPOCHS:
        print(f"  {WEIGHT_DIR}/epoch{ep:02d}/", flush=True)

    return model


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="布局特征模型训练")
    parser.add_argument('--batch-size', type=int, default=BATCH_SIZE)
    parser.add_argument('--lr', type=float, default=LR)
    parser.add_argument('--epochs', type=int, default=EPOCHS)
    args = parser.parse_args()

    BATCH_SIZE = args.batch_size
    LR = args.lr
    EPOCHS = args.epochs
    SAVE_EPOCHS = sorted(set([1] + [e for e in [2, 3, 5, 10, 20, 30, 50]
                                 if e <= EPOCHS]))

    train(args)
