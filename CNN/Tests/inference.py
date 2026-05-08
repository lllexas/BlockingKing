"""
纯 NumPy 推理层 — 与 Unity C# 手动推理运算完全对齐
Conv2d (pad1 / nopad), FC, ReLU, Sigmoid, Softmax
"""

import numpy as np


# ── 激活函数 ──────────────────────────────────────────────
def relu(x):
    return np.maximum(x, 0)


def sigmoid(x):
    # 数值稳定版
    x = np.clip(x, -50, 50)
    return 1.0 / (1.0 + np.exp(-x))


def softmax(x, axis=-1):
    x = x - x.max(axis=axis, keepdims=True)
    e = np.exp(x)
    return e / e.sum(axis=axis, keepdims=True)


# ── 全连接 ────────────────────────────────────────────────
def fc_layer(x, weight, bias):
    """x: [D_in], weight: [D_in, D_out], bias: [D_out] → [D_out]"""
    return x @ weight + bias


# ── Conv2d (带 padding) ──────────────────────────────────
def conv2d_pad1(x, weight, bias):
    """same padding (3×3 kernel, stride=1)
    x:      [C_in, H, W]
    weight: [kH, kW, C_in, C_out]
    bias:   [C_out]
    → [C_out, H, W]
    """
    kH, kW, C_in, C_out = weight.shape
    Hi, Wi = x.shape[1], x.shape[2]
    pad_h, pad_w = kH // 2, kW // 2
    x_pad = np.pad(x, ((0, 0), (pad_h, pad_h), (pad_w, pad_w)), mode='constant')

    out = np.zeros((C_out, Hi, Wi), dtype=np.float32)
    for oc in range(C_out):
        for ic in range(C_in):
            w_ic_oc = weight[:, :, ic, oc]
            for kh in range(kH):
                for kw in range(kW):
                    out[oc] += x_pad[ic, kh:kh + Hi, kw:kw + Wi] * w_ic_oc[kh, kw]
        out[oc] += bias[oc]
    return out


# ── Conv2d (无 padding) ──────────────────────────────────
def conv2d_nopad(x, weight, bias):
    """valid padding (3×3 kernel, stride=1) — MapCNN 专用
    x:      [C_in, H, W]
    weight: [kH, kW, C_in, C_out]
    bias:   [C_out]
    → [C_out, H-2, W-2]
    """
    kH, kW, C_in, C_out = weight.shape
    Hi, Wi = x.shape[1], x.shape[2]
    Ho, Wo = Hi - kH + 1, Wi - kW + 1

    out = np.zeros((C_out, Ho, Wo), dtype=np.float32)
    for oc in range(C_out):
        for ic in range(C_in):
            w_ic_oc = weight[:, :, ic, oc]
            for kh in range(kH):
                for kw in range(kW):
                    out[oc] += x[ic, kh:kh + Ho, kw:kw + Wo] * w_ic_oc[kh, kw]
        out[oc] += bias[oc]
    return out


# ── Conv2d (1×1 kernel) ──────────────────────────────────
def conv2d_1x1(x, weight, bias):
    """1×1 kernel = per-location linear projection
    weight: [1, 1, C_in, C_out]
    """
    _, _, C_in, C_out = weight.shape
    w = weight[0, 0]  # [C_in, C_out]
    out = np.tensordot(x.transpose(1, 2, 0), w, axes=1)  # [H, W, C_out]
    out = out.transpose(2, 0, 1) + bias[:, None, None]    # [C_out, H, W]
    return out
