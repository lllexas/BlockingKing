"""
CNN 地形生成 — 训练脚本
5 类输出: 墙/地板/目标/箱子/箱子在目标
架构: 7×7 → Conv1(3×3,1→8) → Conv2(3×3,8→16) → Conv3(3×3,16→16) → FC(16→5)
感受野 7×7，参数 ~3650，保存 epoch 2/3/5/8 供 WFC 实测
"""

import os, sys
import numpy as np
import torch
import torch.nn as nn
from torch.utils.data import TensorDataset, DataLoader

# ── 超参数 ────────────────────────────────────────────────
BATCH_SIZE     = 512
LR             = 0.001
EPOCHS         = 20       # 固定跑 20 轮
VAL_SPLIT      = 0.1
RANDOM_SEED    = 42
SAVE_EPOCHS    = [2, 3, 5, 8]   # 保存哪些轮次的权重

_SCRIPT_DIR    = os.path.dirname(os.path.abspath(__file__))
DATA_DIR       = os.path.join(_SCRIPT_DIR, "../data")
WEIGHT_DIR     = os.path.join(_SCRIPT_DIR, "../weights")
TILE_MAX_ID    = 6.0             # 最大 tile ID (* = 6)
NUM_CLASSES    = 5


# ── 模型 ──────────────────────────────────────────────────
# 3 层 3×3 卷积叠出 7×7 感受野，覆盖整个窗口
# Layer 1: 学边界 (wall-floor edge)
# Layer 2: 学转角 / T 字口 (corner, junction)
# Layer 3: 学空间结构 (room vs corridor)
# 总参数 ~3650，C# 纯 CPU 推理 <1ms
class SokobanCNN(nn.Module):
    def __init__(self):
        super().__init__()
        self.conv1 = nn.Conv2d(1, 8, 3)    # 7×7 → 5×5 (rf=3)
        self.conv2 = nn.Conv2d(8, 16, 3)   # 5×5 → 3×3 (rf=5)
        self.conv3 = nn.Conv2d(16, 16, 3)  # 3×3 → 1×1 (rf=7)
        self.fc    = nn.Linear(16, NUM_CLASSES)

    def forward(self, x):
        x = torch.relu(self.conv1(x))
        x = torch.relu(self.conv2(x))
        x = torch.relu(self.conv3(x))
        return self.fc(x.view(x.size(0), -1))


# ── 权重导出 ──────────────────────────────────────────────
def export_weights(model, out_dir, epoch=0, val_loss=0, val_acc=0):
    """PyTorch → .bin, 适配 Unity C#: Conv [out,in,H,W] → [H,W,in,out]"""
    os.makedirs(out_dir, exist_ok=True)
    state = model.state_dict()

    def save_conv(name, w, b):
        w.cpu().numpy().transpose(2, 3, 1, 0).tofile(f"{out_dir}/{name}_weight.bin")
        b.cpu().numpy().tofile(f"{out_dir}/{name}_bias.bin")

    save_conv("conv1", state['conv1.weight'], state['conv1.bias'])
    save_conv("conv2", state['conv2.weight'], state['conv2.bias'])
    save_conv("conv3", state['conv3.weight'], state['conv3.bias'])
    state['fc.weight'].cpu().numpy().T.tofile(f"{out_dir}/dense_weight.bin")
    state['fc.bias'].cpu().numpy().tofile(f"{out_dir}/dense_bias.bin")

    # 日记
    from datetime import datetime
    n_params = sum(p.numel() for p in model.parameters())
    with open(f"{out_dir}/diary.txt", 'w', encoding='utf-8') as f:
        f.write(f"生成时间: {datetime.now():%Y-%m-%d %H:%M:%S}\n")
        f.write(f"Epoch: {epoch}\n")
        f.write(f"架构: Conv1(3×3,1→8) → Conv2(3×3,8→16) → Conv3(3×3,16→16) → FC(16→5)\n")
        f.write(f"参数: {n_params}\n")
        f.write(f"BATCH: {BATCH_SIZE}, LR: {LR}\n")
        f.write(f"Val Loss: {val_loss:.4f}, Val Acc: {val_acc:.2%}\n")


# ── 训练 ──────────────────────────────────────────────────
def train():
    torch.manual_seed(RANDOM_SEED)
    np.random.seed(RANDOM_SEED)
    torch.backends.cudnn.benchmark = True

    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f"设备: {device}", flush=True)

    # ── 加载 ─────────────────────────────────────────
    print("加载数据...", flush=True)
    X = np.load(f"{DATA_DIR}/X.npy").astype(np.float32) / TILE_MAX_ID
    y = np.load(f"{DATA_DIR}/y.npy").astype(np.int64)

    n_val = int(len(X) * VAL_SPLIT)
    idx = np.random.permutation(len(X))
    X_train, X_val = X[idx[:-n_val]], X[idx[-n_val:]]
    y_train, y_val = y[idx[:-n_val]], y[idx[-n_val:]]

    print(f"  训练: {len(X_train):,},  验证: {len(X_val):,}", flush=True)
    print(f"  类别数: {NUM_CLASSES} (墙/地板/目标/箱子/箱子在目标)", flush=True)

    X_train_t = torch.from_numpy(X_train).unsqueeze(1)
    y_train_t = torch.from_numpy(y_train)
    X_val_t   = torch.from_numpy(X_val).unsqueeze(1)
    y_val_t   = torch.from_numpy(y_val)

    train_dl = DataLoader(TensorDataset(X_train_t, y_train_t),
                          batch_size=BATCH_SIZE, shuffle=True, pin_memory=True)
    val_dl   = DataLoader(TensorDataset(X_val_t, y_val_t),
                          batch_size=BATCH_SIZE * 4, pin_memory=True)

    # ── 模型 ─────────────────────────────────────────
    model = SokobanCNN().to(device)
    n_params = sum(p.numel() for p in model.parameters())
    print(f"  参数: {n_params}", flush=True)

    criterion = nn.CrossEntropyLoss()   # 无类别权重
    optimizer = torch.optim.Adam(model.parameters(), lr=LR)

    # ── 训练 ─────────────────────────────────────────
    print(f"\n训练 {EPOCHS} 轮, 保存: epoch {SAVE_EPOCHS}", flush=True)
    print("-" * 60, flush=True)
    print(f"{'Ep':>4s}  {'Train Loss':>10s}  {'Train Acc':>9s}  "
          f"{'Val Loss':>8s}  {'Val Acc':>7s}", flush=True)
    print("-" * 60, flush=True)

    for epoch in range(1, EPOCHS + 1):
        # train
        model.train()
        train_loss, train_correct, train_total = 0.0, 0, 0
        for bx, by in train_dl:
            bx, by = bx.to(device, non_blocking=True), by.to(device, non_blocking=True)
            optimizer.zero_grad()
            logits = model(bx)
            loss = criterion(logits, by)
            loss.backward()
            optimizer.step()
            train_loss += loss.item() * bx.size(0)
            train_correct += (logits.argmax(1) == by).sum().item()
            train_total += bx.size(0)

        train_loss /= train_total
        train_acc = train_correct / train_total

        # val
        model.eval()
        val_loss, val_correct, val_total = 0.0, 0, 0
        with torch.no_grad():
            for bx, by in val_dl:
                bx, by = bx.to(device, non_blocking=True), by.to(device, non_blocking=True)
                logits = model(bx)
                val_loss += criterion(logits, by).item() * bx.size(0)
                val_correct += (logits.argmax(1) == by).sum().item()
                val_total += bx.size(0)

        val_loss /= val_total
        val_acc = val_correct / val_total

        print(f"{epoch:>4d}  {train_loss:>10.4f}  {train_acc:>8.2%}  "
              f"{val_loss:>8.4f}  {val_acc:>7.2%}", flush=True)

        # 保存 checkpoint
        if epoch in SAVE_EPOCHS:
            ckpt_dir = f"{WEIGHT_DIR}/epoch{epoch:02d}"
            export_weights(model, ckpt_dir, epoch=epoch,
                           val_loss=val_loss, val_acc=val_acc)
            print(f"  → 已保存 epoch {epoch} 权重", flush=True)

    print("-" * 60, flush=True)

    # ── 最终各类别精度 ────────────────────────────────
    model.eval()
    all_preds, all_labels = [], []
    with torch.no_grad():
        for bx, by in val_dl:
            bx = bx.to(device)
            all_preds.append(model(bx).argmax(1).cpu())
            all_labels.append(by)

    all_preds = torch.cat(all_preds)
    all_labels = torch.cat(all_labels)

    cls_names = ['# 墙', '  地板', '. 目标', '$ 箱', '* 箱在目标']
    print("\n各类别验证准确率 (最终):", flush=True)
    for c in range(NUM_CLASSES):
        mask = all_labels == c
        if mask.sum() > 0:
            acc = (all_preds[mask] == c).sum().item() / mask.sum().item()
            print(f"  {cls_names[c]:<12s}: {acc:.2%}  (n={mask.sum().item():,})", flush=True)

    print(f"\n权重输出目录:", flush=True)
    for ep in SAVE_EPOCHS:
        print(f"  {WEIGHT_DIR}/epoch{ep:02d}/", flush=True)

    return model


if __name__ == '__main__':
    train()
