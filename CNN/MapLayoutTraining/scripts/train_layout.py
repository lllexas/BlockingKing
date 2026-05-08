"""
布局模型 — 训练脚本
输入: [2, H, W] (n/MAX_N 常数 + valid_mask)
输出: [2, H, W] (箱概率图 + 目标概率图)
全卷积 same padding → 支持变尺寸推理
"""

import os, sys, argparse
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
SAVE_EPOCHS    = [1, 2, 3, 5, 10, 20, 30]
POS_WEIGHT     = 15.0          # BCE 正样本权重（箱/目标格极稀疏）

_SCRIPT_DIR    = os.path.dirname(os.path.abspath(__file__))
DATA_PATH      = os.path.join(_SCRIPT_DIR, "../data/data.npz")
WEIGHT_DIR     = os.path.join(_SCRIPT_DIR, "../weights")


# ── 模型 ──────────────────────────────────────────────────
# 5 层 3×3 Conv (same padding) → 感受野 11×11
# 1 层 1×1 Conv → 2 通道输出
# 总参数 ~2350
class LayoutCNN(nn.Module):
    def __init__(self):
        super().__init__()
        self.conv1 = nn.Conv2d(2, 6, 3, padding=1)    # rf=3
        self.conv2 = nn.Conv2d(6, 10, 3, padding=1)   # rf=5
        self.conv3 = nn.Conv2d(10, 10, 3, padding=1)  # rf=7
        self.conv4 = nn.Conv2d(10, 6, 3, padding=1)   # rf=9
        self.conv5 = nn.Conv2d(6, 4, 3, padding=1)    # rf=11
        self.conv6 = nn.Conv2d(4, 2, 1)                # rf=11

    def forward(self, x):
        x = torch.relu(self.conv1(x))
        x = torch.relu(self.conv2(x))
        x = torch.relu(self.conv3(x))
        x = torch.relu(self.conv4(x))
        x = torch.relu(self.conv5(x))
        return self.conv6(x)  # logits, sigmoid 在 loss 里


# ── 损失函数 ──────────────────────────────────────────────
def masked_loss(logits, target, mask):
    """masked BCEWithLogitsLoss，仅在 valid_mask==1 处计算"""
    bce = nn.functional.binary_cross_entropy_with_logits(
        logits, target, reduction='none')
    bce = bce * mask
    return bce.sum() / (mask.sum() + 1e-8)


def top_n_recall(probs, target, mask, n_values):
    """Top-N 召回率：取概率最高的 N 格，命中真实箱/目标的比例"""
    recalls_box, recalls_goal = [], []
    probs = probs.detach().cpu().numpy()
    target = target.detach().cpu().numpy()
    mask = mask.detach().cpu().numpy().astype(bool)
    n_values = n_values.detach().cpu().numpy().astype(int)

    for i in range(len(probs)):
        n = n_values[i]
        valid = mask[i, 0]  # [H, W]

        # 箱 channel
        p_box = probs[i, 0].copy()
        p_box[~valid] = -1  # 无效区不参与
        top_idx = np.argsort(p_box.ravel())[-n:]
        recalls_box.append(target[i, 0].ravel()[top_idx].sum() / n)

        # 目标 channel
        p_goal = probs[i, 1].copy()
        p_goal[~valid] = -1
        top_idx = np.argsort(p_goal.ravel())[-n:]
        recalls_goal.append(target[i, 1].ravel()[top_idx].sum() / n)

    return np.mean(recalls_box), np.mean(recalls_goal)


# ── 权重导出 ──────────────────────────────────────────────
def export_weights(model, out_dir, epoch, metrics):
    """PyTorch → .bin, 适配 Unity C#: Conv [out,in,H,W] → [H,W,in,out]"""
    os.makedirs(out_dir, exist_ok=True)
    state = model.state_dict()

    def save_conv(name, w, b):
        w.cpu().numpy().transpose(2, 3, 1, 0).tofile(f"{out_dir}/{name}_weight.bin")
        b.cpu().numpy().tofile(f"{out_dir}/{name}_bias.bin")

    for i in range(1, 6):
        save_conv(f"conv{i}", state[f"conv{i}.weight"], state[f"conv{i}.bias"])

    # conv6: 1×1 [out,in,1,1] → [1,1,in,out]
    w6 = state["conv6.weight"]; b6 = state["conv6.bias"]
    w6.cpu().numpy().transpose(2, 3, 1, 0).tofile(f"{out_dir}/conv6_weight.bin")
    b6.cpu().numpy().tofile(f"{out_dir}/conv6_bias.bin")

    from datetime import datetime
    n_params = sum(p.numel() for p in model.parameters())
    with open(f"{out_dir}/diary.txt", 'w', encoding='utf-8') as f:
        f.write(f"生成时间: {datetime.now():%Y-%m-%d %H:%M:%S}\n")
        f.write(f"Epoch: {epoch}\n")
        f.write(f"架构: Conv(2→6→10→10→6→4)→Conv(4→2)\n")
        f.write(f"参数: {n_params}\n")
        f.write(f"BATCH: {BATCH_SIZE}, LR: {LR}\n")
        for k, v in metrics.items():
            f.write(f"{k}: {v:.6f}\n")


# ── 训练 ──────────────────────────────────────────────────
def train(args):
    torch.manual_seed(RANDOM_SEED)
    np.random.seed(RANDOM_SEED)
    torch.backends.cudnn.benchmark = True

    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f"设备: {device}", flush=True)

    # ── 加载 ─────────────────────────────────────────
    print("加载数据...", flush=True)
    data = np.load(DATA_PATH)
    X, y = data['X'], data['y']  # [N,2,20,20]
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
    model = LayoutCNN().to(device)
    n_params = sum(p.numel() for p in model.parameters())
    print(f"  参数: {n_params}", flush=True)

    optimizer = torch.optim.Adam(model.parameters(), lr=LR)

    print(f"\n训练 {EPOCHS} 轮, 保存: epoch {SAVE_EPOCHS}", flush=True)
    print("-" * 75, flush=True)
    header = (f"{'Ep':>4s}  {'Train Loss':>10s}  "
              f"{'Val Loss':>8s}  {'Box R@N':>7s}  {'Goal R@N':>7s}")
    print(header, flush=True)
    print("-" * 75, flush=True)

    best_val_loss = float('inf')

    for epoch in range(1, EPOCHS + 1):
        # ── train ──
        model.train()
        train_loss = 0.0
        for bx, by in train_dl:
            bx, by = bx.to(device, non_blocking=True), by.to(device, non_blocking=True)
            mask = bx[:, 1:2]  # valid_mask
            optimizer.zero_grad()
            logits = model(bx)
            loss = masked_loss(logits, by, mask)
            loss.backward()
            optimizer.step()
            train_loss += loss.item() * bx.size(0)

        train_loss /= len(X_train)

        # ── val ──
        model.eval()
        val_loss = 0.0
        all_probs, all_targets, all_masks, all_n = [], [], [], []
        with torch.no_grad():
            for bx, by in val_dl:
                bx, by = bx.to(device), by.to(device)
                mask = bx[:, 1:2]
                n_vals = (bx[:, 0, 0, 0] * MAX_N).long()  # n per sample

                logits = model(bx)
                val_loss += masked_loss(logits, by, mask).item() * bx.size(0)
                all_probs.append(torch.sigmoid(logits))
                all_targets.append(by)
                all_masks.append(mask)
                all_n.append(n_vals)

        val_loss /= len(X_val)
        probs = torch.cat(all_probs)
        targets = torch.cat(all_targets)
        masks = torch.cat(all_masks)
        ns = torch.cat(all_n)

        box_recall, goal_recall = top_n_recall(probs, targets, masks, ns)

        print(f"{epoch:>4d}  {train_loss:>10.4f}  "
              f"{val_loss:>8.4f}  {box_recall:>6.2%}  {goal_recall:>6.2%}",
              flush=True)

        # ── save ──
        if epoch in SAVE_EPOCHS:
            ckpt_dir = f"{WEIGHT_DIR}/epoch{epoch:02d}"
            metrics = {'val_loss': val_loss, 'box_recall': box_recall,
                       'goal_recall': goal_recall}
            export_weights(model, ckpt_dir, epoch, metrics)
            print(f"  → 已保存 epoch {epoch} 权重", flush=True)

        if val_loss < best_val_loss:
            best_val_loss = val_loss

    print("-" * 75, flush=True)

    # ── 最终报告 ─────────────────────────────────────
    print(f"\n最终验证指标:")
    print(f"  Best Val Loss: {best_val_loss:.6f}")
    print(f"  Box Top-N Recall: {box_recall:.2%}")
    print(f"  Goal Top-N Recall: {goal_recall:.2%}")

    print(f"\n权重输出目录:", flush=True)
    for ep in SAVE_EPOCHS:
        print(f"  {WEIGHT_DIR}/epoch{ep:02d}/", flush=True)

    return model


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="布局模型训练")
    parser.add_argument('--batch-size', type=int, default=BATCH_SIZE)
    parser.add_argument('--lr', type=float, default=LR)
    parser.add_argument('--epochs', type=int, default=EPOCHS)
    parser.add_argument('--pos-weight', type=float, default=POS_WEIGHT)
    args = parser.parse_args()

    BATCH_SIZE = args.batch_size
    LR = args.lr
    EPOCHS = args.epochs
    POS_WEIGHT = args.pos_weight
    SAVE_EPOCHS = sorted(set([1] + [e for e in [3,5,10,20,30] if e <= EPOCHS]))

    train(args)
