"""
模型加载器 — 从 .bin 文件加载权重（纯 NumPy）
"""

import os
import numpy as np


# ── BoxFeature ────────────────────────────────────────────
def load_box_feature(weight_dir):
    """FC 网络: 3→16→32→32→60, weight=[in, out]"""
    m = {}
    m['fc1_w'] = np.fromfile(f"{weight_dir}/fc1_weight.bin", dtype=np.float32).reshape(3, 16)
    m['fc1_b'] = np.fromfile(f"{weight_dir}/fc1_bias.bin", dtype=np.float32)
    m['fc2_w'] = np.fromfile(f"{weight_dir}/fc2_weight.bin", dtype=np.float32).reshape(16, 32)
    m['fc2_b'] = np.fromfile(f"{weight_dir}/fc2_bias.bin", dtype=np.float32)
    m['fc3_w'] = np.fromfile(f"{weight_dir}/fc3_weight.bin", dtype=np.float32).reshape(32, 32)
    m['fc3_b'] = np.fromfile(f"{weight_dir}/fc3_bias.bin", dtype=np.float32)
    m['fc4_w'] = np.fromfile(f"{weight_dir}/fc4_weight.bin", dtype=np.float32).reshape(32, 60)
    m['fc4_b'] = np.fromfile(f"{weight_dir}/fc4_bias.bin", dtype=np.float32)
    return m


# ── WallFill ──────────────────────────────────────────────
def load_wall_fill(weight_dir):
    """Conv 网络: 4→6→10→10→6→4→1, weight=[kH, kW, in, out]"""
    m = {}
    m['conv1_w'] = np.fromfile(f"{weight_dir}/conv1_weight.bin", dtype=np.float32).reshape(3, 3, 4, 6)
    m['conv1_b'] = np.fromfile(f"{weight_dir}/conv1_bias.bin", dtype=np.float32)
    m['conv2_w'] = np.fromfile(f"{weight_dir}/conv2_weight.bin", dtype=np.float32).reshape(3, 3, 6, 10)
    m['conv2_b'] = np.fromfile(f"{weight_dir}/conv2_bias.bin", dtype=np.float32)
    m['conv3_w'] = np.fromfile(f"{weight_dir}/conv3_weight.bin", dtype=np.float32).reshape(3, 3, 10, 10)
    m['conv3_b'] = np.fromfile(f"{weight_dir}/conv3_bias.bin", dtype=np.float32)
    m['conv4_w'] = np.fromfile(f"{weight_dir}/conv4_weight.bin", dtype=np.float32).reshape(3, 3, 10, 6)
    m['conv4_b'] = np.fromfile(f"{weight_dir}/conv4_bias.bin", dtype=np.float32)
    m['conv5_w'] = np.fromfile(f"{weight_dir}/conv5_weight.bin", dtype=np.float32).reshape(3, 3, 6, 4)
    m['conv5_b'] = np.fromfile(f"{weight_dir}/conv5_bias.bin", dtype=np.float32)
    m['conv6_w'] = np.fromfile(f"{weight_dir}/conv6_weight.bin", dtype=np.float32).reshape(1, 1, 4, 1)
    m['conv6_b'] = np.fromfile(f"{weight_dir}/conv6_bias.bin", dtype=np.float32)
    return m


# ── BoxPlacer / GoalPlacer ─────────────────────────────────
# 同架构: Conv(2→6→10→10→6→4→1)
def _load_conv_net(weight_dir):
    m = {}
    for i, (cin, cout) in enumerate([(2,6),(6,10),(10,10),(10,6),(6,4)], start=1):
        m[f'conv{i}_w'] = np.fromfile(f"{weight_dir}/conv{i}_weight.bin", dtype=np.float32).reshape(3,3,cin,cout)
        m[f'conv{i}_b'] = np.fromfile(f"{weight_dir}/conv{i}_bias.bin", dtype=np.float32)
    m['conv6_w'] = np.fromfile(f"{weight_dir}/conv6_weight.bin", dtype=np.float32).reshape(1,1,4,1)
    m['conv6_b'] = np.fromfile(f"{weight_dir}/conv6_bias.bin", dtype=np.float32)
    return m

load_box_placer = _load_conv_net
load_goal_placer = _load_conv_net

# ── MapCNN ────────────────────────────────────────────────
def load_map_cnn(weight_dir):
    """Conv(1→8→16→16) + FC(16→5), conv=[kH,kW,in,out], dense=[in,out]"""
    m = {}
    m['conv1_w'] = np.fromfile(f"{weight_dir}/conv1_weight.bin", dtype=np.float32).reshape(3, 3, 1, 8)
    m['conv1_b'] = np.fromfile(f"{weight_dir}/conv1_bias.bin", dtype=np.float32)
    m['conv2_w'] = np.fromfile(f"{weight_dir}/conv2_weight.bin", dtype=np.float32).reshape(3, 3, 8, 16)
    m['conv2_b'] = np.fromfile(f"{weight_dir}/conv2_bias.bin", dtype=np.float32)
    m['conv3_w'] = np.fromfile(f"{weight_dir}/conv3_weight.bin", dtype=np.float32).reshape(3, 3, 16, 16)
    m['conv3_b'] = np.fromfile(f"{weight_dir}/conv3_bias.bin", dtype=np.float32)
    m['dense_w'] = np.fromfile(f"{weight_dir}/dense_weight.bin", dtype=np.float32).reshape(16, 5)
    m['dense_b'] = np.fromfile(f"{weight_dir}/dense_bias.bin", dtype=np.float32)
    return m
