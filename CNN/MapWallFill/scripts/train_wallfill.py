"""
墙壁填充模型 — 训练脚本
输入: [4, H, W] (箱+目标+n/5+mask)
输出: [1, H, W] (墙概率)
"""

import os, sys, argparse, copy
import numpy as np
import torch
import torch.nn as nn
from torch.utils.data import TensorDataset, DataLoader

BATCH_SIZE     = 512
LR             = 0.001
EPOCHS         = 30
VAL_SPLIT      = 0.1
RANDOM_SEED    = 42
MAX_SIZE       = 20
MAX_N          = 5
SAVE_EPOCHS    = [1, 2, 3, 5, 10, 20, 30]
PATIENCE       = 8

_SCRIPT_DIR    = os.path.dirname(os.path.abspath(__file__))
DATA_PATH      = os.path.join(_SCRIPT_DIR, "../data/data.npz")
WEIGHT_DIR     = os.path.join(_SCRIPT_DIR, "../weights")


# ── 模型 ──────────────────────────────────────────────────
class WallFillCNN(nn.Module):
    """全卷积, 4ch→1ch, ~2450 参数"""
    def __init__(self):
        super().__init__()
        self.conv1 = nn.Conv2d(4, 6, 3, padding=1)
        self.conv2 = nn.Conv2d(6, 10, 3, padding=1)
        self.conv3 = nn.Conv2d(10, 10, 3, padding=1)
        self.conv4 = nn.Conv2d(10, 6, 3, padding=1)
        self.conv5 = nn.Conv2d(6, 4, 3, padding=1)
        self.conv6 = nn.Conv2d(4, 1, 1)

    def forward(self, x):
        x = torch.relu(self.conv1(x))
        x = torch.relu(self.conv2(x))
        x = torch.relu(self.conv3(x))
        x = torch.relu(self.conv4(x))
        x = torch.relu(self.conv5(x))
        return self.conv6(x)


# ── 损失 ──────────────────────────────────────────────────
def masked_bce(logits, target, mask):
    bce = nn.functional.binary_cross_entropy_with_logits(
        logits, target, reduction='none')
    return (bce * mask).sum() / (mask.sum() + 1e-8)


def wall_metrics(probs, target, mask):
    """墙像素准确率 (阈值 0.5)"""
    probs = probs.detach().cpu().numpy()
    target = target.detach().cpu().numpy()
    mask = mask.detach().cpu().numpy().astype(bool)

    total = mask.sum()
    pred_wall = (probs > 0.5) & mask
    target_wall = (target > 0.5) & mask

    correct = ((pred_wall == target_wall) & mask).sum()  # 仅有效格
    acc = correct / total

    tp = (pred_wall & target_wall).sum()
    prec = tp / (pred_wall.sum() + 1e-8)
    rec = tp / (target_wall.sum() + 1e-8)

    return acc, prec, rec


# ── 权重导出 ──────────────────────────────────────────────
def export_weights(model, out_dir, epoch, metrics):
    os.makedirs(out_dir, exist_ok=True)
    state = model.state_dict()

    def save_conv(name, w, b):
        w.cpu().numpy().transpose(2, 3, 1, 0).tofile(f"{out_dir}/{name}_weight.bin")
        b.cpu().numpy().tofile(f"{out_dir}/{name}_bias.bin")

    for i in range(1, 6):
        save_conv(f"conv{i}", state[f"conv{i}.weight"], state[f"conv{i}.bias"])
    w6 = state["conv6.weight"]; b6 = state["conv6.bias"]
    w6.cpu().numpy().transpose(2, 3, 1, 0).tofile(f"{out_dir}/conv6_weight.bin")
    b6.cpu().numpy().tofile(f"{out_dir}/conv6_bias.bin")

    from datetime import datetime
    n_params = sum(p.numel() for p in model.parameters())
    with open(f"{out_dir}/diary.txt", 'w', encoding='utf-8') as f:
        f.write(f"生成: {datetime.now():%Y-%m-%d %H:%M:%S}\n")
        f.write(f"Epoch: {epoch}\n")
        f.write(f"架构: Conv(4→6→10→10→6→4)→Conv(4→1)\n")
        f.write(f"参数: {n_params}\n")
        for k, v in metrics.items():
            f.write(f"{k}: {v:.6f}\n")


# ── 训练 ──────────────────────────────────────────────────
def train(args):
    torch.manual_seed(RANDOM_SEED)
    np.random.seed(RANDOM_SEED)

    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f"设备: {device}", flush=True)

    print("加载...", flush=True)
    data = np.load(DATA_PATH)
    X, y = data['X'], data['y']
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

    model = WallFillCNN().to(device)
    n_params = sum(p.numel() for p in model.parameters())
    print(f"  参数: {n_params}", flush=True)

    optimizer = torch.optim.Adam(model.parameters(), lr=LR)
    start_epoch = 1

    if args.resume:
        ckpt = torch.load(args.resume, map_location=device, weights_only=False)
        model.load_state_dict(ckpt['model'])
        optimizer.load_state_dict(ckpt['optimizer'])
        start_epoch = ckpt.get('epoch', 0) + 1
        print(f"  续跑: epoch {start_epoch}, prev loss={ckpt.get('val_loss','?'):.6f}", flush=True)

    print(f"\n训练 {EPOCHS} 轮, patience={PATIENCE}", flush=True)
    print("-" * 75)
    header = (f"{'Ep':>4s}  {'Train':>8s}  {'Val':>8s}  "
              f"{'Wall Acc':>8s}  {'Prec':>6s}  {'Rec':>6s}  {'Stale':>5s}")
    print(header)
    print("-" * 75)

    best_val = float('inf')
    best_state = None
    stale = 0
    stopped = False

    for epoch in range(start_epoch, EPOCHS + 1):
        model.train()
        tr_loss = 0.0
        for bx, by in train_dl:
            bx, by = bx.to(device, non_blocking=True), by.to(device, non_blocking=True)
            mask = bx[:, 3:4]
            optimizer.zero_grad()
            logits = model(bx)
            loss = masked_bce(logits, by, mask)
            loss.backward()
            optimizer.step()
            tr_loss += loss.item() * bx.size(0)
        tr_loss /= len(X_train)

        model.eval()
        vl_loss = 0.0
        all_logits, all_y, all_mask = [], [], []
        with torch.no_grad():
            for bx, by in val_dl:
                bx, by = bx.to(device), by.to(device)
                mask = bx[:, 3:4]
                logits = model(bx)
                vl_loss += masked_bce(logits, by, mask).item() * bx.size(0)
                all_logits.append(torch.sigmoid(logits))
                all_y.append(by)
                all_mask.append(mask)

        vl_loss /= len(X_val)
        probs = torch.cat(all_logits)
        targets = torch.cat(all_y)
        masks = torch.cat(all_mask)

        acc, prec, rec = wall_metrics(probs, targets, masks)

        improved = vl_loss < best_val - 1e-6
        if improved:
            best_val = vl_loss
            best_state = copy.deepcopy(model.state_dict())
            stale = 0
        else:
            stale += 1

        print(f"{epoch:>4d}  {tr_loss:>8.4f}  {vl_loss:>8.4f}  "
              f"{acc:>7.2%}  {prec:>5.2%}  {rec:>5.2%}  "
              f"{stale:>4d}{' *' if improved else ''}",
              flush=True)

        if epoch in SAVE_EPOCHS:
            ckpt = f"{WEIGHT_DIR}/epoch{epoch:02d}"
            export_weights(model, ckpt, epoch,
                          {'val_loss': vl_loss, 'acc': acc,
                           'prec': prec, 'rec': rec})
            print(f"  → 已存 epoch {epoch}", flush=True)

        # 每轮存续跑检查点
        torch.save({
            'epoch': epoch, 'model': model.state_dict(),
            'optimizer': optimizer.state_dict(), 'val_loss': vl_loss,
        }, f"{WEIGHT_DIR}/latest.pt")

        if stale >= PATIENCE:
            print(f"\n  Early stop @ {epoch} (best={best_val:.6f})", flush=True)
            stopped = True
            break

    print("-" * 75)

    if stopped and best_state is not None:
        model.load_state_dict(best_state)

    print(f"\n最终 (best epoch):")
    print(f"  Val Loss: {best_val:.6f}")
    print(f"  Wall Acc: {acc:.2%}, Prec: {prec:.2%}, Rec: {rec:.2%}")
    if stopped:
        print(f"  Early stop: 是 (patience={PATIENCE})")

    for ep in [e for e in SAVE_EPOCHS if e <= (epoch if stopped else EPOCHS)]:
        print(f"  {WEIGHT_DIR}/epoch{ep:02d}/", flush=True)

    return model


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="墙壁填充模型训练")
    parser.add_argument('--batch-size', type=int, default=BATCH_SIZE)
    parser.add_argument('--lr', type=float, default=LR)
    parser.add_argument('--epochs', type=int, default=EPOCHS)
    parser.add_argument('--resume', type=str, default='',
                        help='续跑检查点路径 (.pt)')
    args = parser.parse_args()
    BATCH_SIZE = args.batch_size
    LR = args.lr
    EPOCHS = args.epochs
    SAVE_EPOCHS = sorted(set([1] + [e for e in [2, 3, 5, 10, 20, 30]
                                 if e <= EPOCHS]))
    train(args)
