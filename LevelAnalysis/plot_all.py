#!/usr/bin/env python3
"""
BlockingKing 关卡特征全维度分析
三层面：单变量分布 / 双变量关系 / 来源目录对比
"""

import os, csv, sys
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.gridspec import GridSpec
from matplotlib.colors import LogNorm
import matplotlib.ticker as mticker
from collections import Counter, defaultdict
from scipy.spatial.distance import pdist, squareform
from scipy.cluster.hierarchy import dendrogram, linkage

# ── Setup ──────────────────────────────────────────────
OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'output')
os.makedirs(OUT_DIR, exist_ok=True)

# Chinese font
plt.rcParams['font.sans-serif'] = ['Microsoft YaHei', 'SimHei', 'DejaVu Sans']
plt.rcParams['axes.unicode_minus'] = False
plt.rcParams['figure.dpi'] = 120
plt.rcParams['savefig.dpi'] = 150
plt.rcParams['savefig.bbox'] = 'tight'

# ── Load Data ──────────────────────────────────────────
CSV_PATH = r"G:\ProjectOfGame\BlockingKing\Assets\Resources\Levels\level_features.csv"
rows = []
with open(CSV_PATH, 'r', encoding='utf-8-sig') as f:
    for d in csv.DictReader(f):
        # convert numeric fields
        for k in ['width','height','area','wall_count','wall_rate','box_count',
                   'box_rate','target_count','box_on_target_count',
                   'effective_box_count','effective_box_rate']:
            d[k] = float(d[k])
        # extract source dir
        parts = d['path'].split('/')
        idx = parts.index('Levels') if 'Levels' in parts else -1
        d['source'] = parts[idx+1] if idx >= 0 and idx+1 < len(parts) else 'Unknown'
        d['aspect_ratio'] = d['width'] / d['height']
        d['log_area'] = np.log10(d['area'])
        d['difficulty_bucket'] = 'hard' if d['effective_box_count'] > 8 else (
            'medium' if d['effective_box_count'] > 3 else 'simple')
        d['emptiness'] = 1.0 - d['wall_rate']  # walkable space ratio
        rows.append(d)

print(f"Loaded {len(rows)} levels from {len(set(r['source'] for r in rows))} sources")

# source order (by level count desc)
src_counts = Counter(r['source'] for r in rows)
SRC_ORDER = [s for s, _ in src_counts.most_common()]
SRC_COLORS = plt.cm.tab20(np.linspace(0, 1, len(SRC_ORDER)))
SRC_CMAP = {s: SRC_COLORS[i] for i, s in enumerate(SRC_ORDER)}

# ── Helper ─────────────────────────────────────────────
def save_and_close(fig, name):
    path = os.path.join(OUT_DIR, name)
    fig.savefig(path)
    plt.close(fig)
    print(f"  -> {name}")

# ═══════════════════════════════════════════════════════
# LAYER 1: 单变量分布
# ═══════════════════════════════════════════════════════

def layer1_distributions():
    """Histograms and bar charts for individual features."""
    
    # Figure 1-1: grid dimensions
    fig, axes = plt.subplots(2, 2, figsize=(12, 9))
    
    # width
    axes[0,0].hist([r['width'] for r in rows], bins=40, color='steelblue', edgecolor='white', alpha=0.85)
    axes[0,0].axvline(np.median([r['width'] for r in rows]), color='red', linestyle='--', label=f"中位数={np.median([r['width'] for r in rows]):.0f}")
    axes[0,0].set_xlabel('宽度 (格子)')
    axes[0,0].set_ylabel('关卡数')
    axes[0,0].set_title('地图宽度分布')
    axes[0,0].legend(fontsize=8)
    
    # height
    axes[0,1].hist([r['height'] for r in rows], bins=40, color='darkorange', edgecolor='white', alpha=0.85)
    axes[0,1].axvline(np.median([r['height'] for r in rows]), color='red', linestyle='--', label=f"中位数={np.median([r['height'] for r in rows]):.0f}")
    axes[0,1].set_xlabel('高度 (格子)')
    axes[0,1].set_ylabel('关卡数')
    axes[0,1].set_title('地图高度分布')
    axes[0,1].legend(fontsize=8)
    
    # area (log scale bins)
    areas = [r['area'] for r in rows]
    axes[1,0].hist(areas, bins=50, color='forestgreen', edgecolor='white', alpha=0.85)
    axes[1,0].axvline(np.median(areas), color='red', linestyle='--', label=f"中位数={np.median(areas):.0f}")
    axes[1,0].set_xlabel('面积 (格子数)')
    axes[1,0].set_ylabel('关卡数')
    axes[1,0].set_title('地图面积分布')
    axes[1,0].legend(fontsize=8)
    
    # aspect ratio
    ars = [r['aspect_ratio'] for r in rows]
    axes[1,1].hist(ars, bins=40, color='mediumpurple', edgecolor='white', alpha=0.85)
    axes[1,1].axvline(1.0, color='gray', linestyle=':', alpha=0.5)
    axes[1,1].axvline(np.median(ars), color='red', linestyle='--', label=f"中位数={np.median(ars):.2f}")
    axes[1,1].set_xlabel('宽高比 (width/height)')
    axes[1,1].set_ylabel('关卡数')
    axes[1,1].set_title('宽高比分布 (>1 偏横向, <1 偏纵向)')
    axes[1,1].legend(fontsize=8)
    
    fig.suptitle('Layer 1-1: 地图几何特征分布', fontsize=14, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L1_geometry.png')
    
    # Figure 1-2: density features
    fig, axes = plt.subplots(2, 2, figsize=(12, 9))
    
    # wall_rate
    wr = [r['wall_rate'] for r in rows]
    axes[0,0].hist(wr, bins=40, color='slategray', edgecolor='white', alpha=0.85)
    axes[0,0].axvline(np.median(wr), color='red', linestyle='--', label=f"中位数={np.median(wr):.3f}")
    axes[0,0].set_xlabel('墙体密度 (wall_rate)')
    axes[0,0].set_ylabel('关卡数')
    axes[0,0].set_title('墙体密度分布')
    axes[0,0].legend(fontsize=8)
    
    # emptiness (1 - wall_rate)
    emp = [1 - r['wall_rate'] for r in rows]
    axes[0,1].hist(emp, bins=40, color='lightseagreen', edgecolor='white', alpha=0.85)
    axes[0,1].axvline(np.median(emp), color='red', linestyle='--', label=f"中位数={np.median(emp):.3f}")
    axes[0,1].set_xlabel('可通行空间占比')
    axes[0,1].set_ylabel('关卡数')
    axes[0,1].set_title('可通行空间分布')
    axes[0,1].legend(fontsize=8)
    
    # effective_box_rate
    ebr = [r['effective_box_rate'] for r in rows]
    axes[1,0].hist(ebr, bins=40, color='indianred', edgecolor='white', alpha=0.85)
    axes[1,0].axvline(np.median(ebr), color='red', linestyle='--', label=f"中位数={np.median(ebr):.3f}")
    axes[1,0].set_xlabel('有效箱密度 (effective_box_rate)')
    axes[1,0].set_ylabel('关卡数')
    axes[1,0].set_title('有效箱密度分布')
    axes[1,0].legend(fontsize=8)
    
    # effective_box_count
    ebc = [r['effective_box_count'] for r in rows]
    axes[1,1].hist(ebc, bins=50, color='teal', edgecolor='white', alpha=0.85)
    axes[1,1].axvline(np.median(ebc), color='red', linestyle='--', label=f"中位数={np.median(ebc):.0f}")
    axes[1,1].set_xlabel('有效箱子数')
    axes[1,1].set_ylabel('关卡数')
    axes[1,1].set_title('有效箱子数分布')
    axes[1,1].legend(fontsize=8)
    
    fig.suptitle('Layer 1-2: 密度与难度特征分布', fontsize=14, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L1_density.png')
    
    # Figure 1-3: difficulty buckets
    fig, axes = plt.subplots(1, 3, figsize=(14, 5))
    buckets = Counter(r['difficulty_bucket'] for r in rows)
    colors_bar = {'simple': 'mediumseagreen', 'medium': 'goldenrod', 'hard': 'indianred'}
    order_en = ['simple', 'medium', 'hard']
    labels_cn = ['简单 (1-3箱)', '中等 (4-8箱)', '困难 (9+箱)']
    vals = [buckets[o] for o in order_en]
    axes[0].bar(labels_cn, vals, color=[colors_bar[o] for o in order_en], edgecolor='white')
    axes[0].set_ylabel('关卡数')
    axes[0].set_title('按需推动箱子数分档')
    for i, v in enumerate(vals):
        axes[0].text(i, v + 3, str(v), ha='center', fontsize=10, fontweight='bold')
    
    # Pie version
    axes[1].pie(vals, labels=labels_cn, autopct='%1.1f%%', 
                colors=[colors_bar[o] for o in order_en], startangle=90)
    axes[1].set_title('难度占比')
    
    # Per-source difficulty stacked bar
    src_diff = defaultdict(lambda: Counter())
    for r in rows:
        src_diff[r['source']][r['difficulty_bucket']] += 1
    src_list = [s for s in SRC_ORDER if s in src_diff]
    bottom_simple = np.zeros(len(src_list))
    bottom_medium = np.zeros(len(src_list))
    simple_vals = [src_diff[s]['simple'] for s in src_list]
    medium_vals = [src_diff[s]['medium'] for s in src_list]
    hard_vals = [src_diff[s]['hard'] for s in src_list]
    axes[2].barh(src_list, simple_vals, color=colors_bar['simple'], label='简单(1-3)', edgecolor='white')
    axes[2].barh(src_list, medium_vals, left=simple_vals, color=colors_bar['medium'], label='中等(4-8)', edgecolor='white')
    left_medium = [s+m for s,m in zip(simple_vals, medium_vals)]
    axes[2].barh(src_list, hard_vals, left=left_medium, color=colors_bar['hard'], label='困难(9+)', edgecolor='white')
    axes[2].set_xlabel('关卡数')
    axes[2].set_title('各来源难度构成')
    axes[2].legend(fontsize=8, loc='lower right')
    
    fig.suptitle('Layer 1-3: 难度分档分析', fontsize=14, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L1_difficulty.png')

# ═══════════════════════════════════════════════════════
# LAYER 2: 双变量关系
# ═══════════════════════════════════════════════════════

def layer2_bivariate():
    """Scatter plots and correlation matrix."""
    
    # Figure 2-1: area vs wall_rate
    fig, axes = plt.subplots(1, 2, figsize=(16, 7))
    
    # All data colored by source
    for src in SRC_ORDER:
        subset = [r for r in rows if r['source'] == src]
        x = [r['log_area'] for r in subset]
        y = [r['wall_rate'] for r in subset]
        axes[0].scatter(x, y, c=[SRC_CMAP[src]], label=src, alpha=0.5, s=12, edgecolors='none')
    axes[0].set_xlabel('log10(面积)')
    axes[0].set_ylabel('墙体密度 (wall_rate)')
    axes[0].set_title('面积 vs 墙体密度（按来源着色）')
    axes[0].legend(fontsize=5, ncol=3, loc='lower left', markerscale=1.5)
    
    # Density heatmap version
    x_all = [r['log_area'] for r in rows]
    y_all = [r['wall_rate'] for r in rows]
    h = axes[1].hist2d(x_all, y_all, bins=40, cmap='YlOrRd', norm=LogNorm())
    axes[1].set_xlabel('log10(面积)')
    axes[1].set_ylabel('墙体密度 (wall_rate)')
    axes[1].set_title('面积 vs 墙体密度（密度热图）')
    plt.colorbar(h[3], ax=axes[1], label='关卡数')
    
    fig.suptitle('Layer 2-1: 面积与墙体密度关系', fontsize=14, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L2_area_vs_wallrate.png')
    
    # Figure 2-2: wall_rate vs box_rate
    fig, axes = plt.subplots(1, 2, figsize=(16, 7))
    
    for src in SRC_ORDER:
        subset = [r for r in rows if r['source'] == src]
        x = [r['wall_rate'] for r in subset]
        y = [r['effective_box_rate'] for r in subset]
        axes[0].scatter(x, y, c=[SRC_CMAP[src]], label=src, alpha=0.5, s=12, edgecolors='none')
    axes[0].set_xlabel('墙体密度 (wall_rate)')
    axes[0].set_ylabel('有效箱密度 (effective_box_rate)')
    axes[0].set_title('墙体密度 vs 箱密度（按来源着色）')
    axes[0].legend(fontsize=5, ncol=3, loc='upper left', markerscale=1.5)
    
    # All points + regression trend
    x_all2 = [r['wall_rate'] for r in rows]
    y_all2 = [r['effective_box_rate'] for r in rows]
    axes[1].scatter(x_all2, y_all2, alpha=0.15, s=8, c='steelblue', edgecolors='none')
    # linear fit
    coeffs = np.polyfit(x_all2, y_all2, 1)
    poly = np.poly1d(coeffs)
    x_line = np.linspace(min(x_all2), max(x_all2), 100)
    axes[1].plot(x_line, poly(x_line), 'r-', linewidth=2, 
                 label=f'y={coeffs[0]:.3f}x+{coeffs[1]:.3f}')
    axes[1].set_xlabel('墙体密度 (wall_rate)')
    axes[1].set_ylabel('有效箱密度 (effective_box_rate)')
    axes[1].set_title('墙体密度 vs 箱密度（趋势拟合）')
    axes[1].legend(fontsize=9)
    
    fig.suptitle('Layer 2-2: 墙体密度与箱密度关系', fontsize=14, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L2_wall_vs_box.png')
    
    # Figure 2-3: width vs height scatter
    fig, ax = plt.subplots(figsize=(10, 8))
    scatter_handles = []
    for src in SRC_ORDER:
        subset = [r for r in rows if r['source'] == src]
        x = [r['width'] for r in subset]
        y = [r['height'] for r in subset]
        s = ax.scatter(x, y, c=[SRC_CMAP[src]], label=src, alpha=0.5, s=15, edgecolors='none')
        scatter_handles.append(s)
    ax.plot([0, 50], [0, 50], 'gray', linestyle=':', alpha=0.4, linewidth=1)
    ax.set_xlabel('宽度')
    ax.set_ylabel('高度')
    ax.set_title('地图宽度 vs 高度（按来源着色）')
    ax.set_xlim(0, 52)
    ax.set_ylim(0, 48)
    ax.legend(fontsize=6, ncol=4, loc='lower right', markerscale=1.5)
    fig.tight_layout()
    save_and_close(fig, 'L2_width_vs_height.png')
    
    # Figure 2-4: correlation matrix
    fig, ax = plt.subplots(figsize=(10, 8))
    corr_fields = ['width', 'height', 'area', 'wall_rate', 'box_count', 
                   'box_rate', 'effective_box_count', 'effective_box_rate',
                   'box_on_target_count', 'aspect_ratio']
    corr_labels = ['宽度', '高度', '面积', '墙密度', '箱总数', 
                   '箱密度', '有效箱数', '有效箱密度', '已就位箱', '宽高比']
    corr_data = np.array([[r[f] for f in corr_fields] for r in rows])
    corr = np.corrcoef(corr_data.T)
    im = ax.imshow(corr, cmap='RdBu_r', vmin=-1, vmax=1, aspect='auto')
    ax.set_xticks(range(len(corr_labels)))
    ax.set_yticks(range(len(corr_labels)))
    ax.set_xticklabels(corr_labels, rotation=45, ha='right', fontsize=9)
    ax.set_yticklabels(corr_labels, fontsize=9)
    # annotate
    for i in range(len(corr_labels)):
        for j in range(len(corr_labels)):
            text = ax.text(j, i, f'{corr[i,j]:.2f}', ha='center', va='center',
                          fontsize=7, color='white' if abs(corr[i,j]) > 0.6 else 'black')
    ax.set_title('特征相关矩阵 (Pearson)', fontsize=13, fontweight='bold')
    plt.colorbar(im, ax=ax, shrink=0.8)
    fig.tight_layout()
    save_and_close(fig, 'L2_correlation_matrix.png')

# ═══════════════════════════════════════════════════════
# LAYER 3: 来源目录对比
# ═══════════════════════════════════════════════════════

def layer3_source_comparison():
    """Per-source statistics and similarity."""
    
    # 3-1: Box plots per source for key features
    fig, axes = plt.subplots(2, 2, figsize=(16, 14))
    
    def make_boxplot(ax, field, title, sources=None):
        if sources is None:
            sources = SRC_ORDER
        data = [[r[field] for r in rows if r['source'] == s] for s in sources]
        bp = ax.boxplot(data, labels=sources, patch_artist=True, showfliers=True, flierprops={'markersize': 2, 'alpha': 0.3})
        for patch, src in zip(bp['boxes'], sources):
            patch.set_facecolor(SRC_CMAP[src])
        ax.set_title(title)
        ax.tick_params(axis='x', rotation=45, labelsize=6)
    
    make_boxplot(axes[0,0], 'wall_rate', '各来源墙体密度对比')
    make_boxplot(axes[0,1], 'effective_box_rate', '各来源有效箱密度对比')
    make_boxplot(axes[1,0], 'area', '各来源面积对比')
    make_boxplot(axes[1,1], 'effective_box_count', '各来源有效箱子数对比')
    
    fig.suptitle('Layer 3-1: 各来源目录关键特征箱线图', fontsize=14, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L3_boxplots.png')
    
    # 3-2: Summary table as bar chart
    fig, axes = plt.subplots(2, 2, figsize=(16, 14))
    metrics = [
        ('wall_rate', '墙体密度均值', 'Oranges'),
        ('effective_box_rate', '有效箱密度均值', 'Reds'),
        ('area', '平均面积', 'Greens'),
        ('effective_box_count', '有效箱子数均值', 'Blues'),
    ]
    for ax, (field, title, cmap_name) in zip(axes.flatten(), metrics):
        means = []
        stds = []
        src_labels = []
        for src in SRC_ORDER:
            vals = [r[field] for r in rows if r['source'] == src]
            if vals:
                means.append(np.mean(vals))
                stds.append(np.std(vals))
                src_labels.append(src)
        colors = plt.cm.get_cmap(cmap_name)(np.linspace(0.3, 0.9, len(src_labels)))
        bars = ax.bar(range(len(src_labels)), means, yerr=stds, 
                      color=colors, edgecolor='white', capsize=3)
        ax.set_xticks(range(len(src_labels)))
        ax.set_xticklabels(src_labels, rotation=45, ha='right', fontsize=7)
        ax.set_title(title)
        ax.set_ylabel(field)
    fig.suptitle('Layer 3-2: 各来源特征均值+标准差对比', fontsize=14, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L3_means.png')
    
    # 3-3: Source similarity matrix (Euclidean distance on normalized features)
    sim_fields = ['wall_rate', 'effective_box_rate', 'box_rate', 'aspect_ratio', 'log_area']
    # Get per-source mean vector
    src_vectors = {}
    for src in SRC_ORDER:
        vec = []
        for f in sim_fields:
            vals = [r[f] for r in rows if r['source'] == src]
            if vals:
                vec.append(np.mean(vals))
            else:
                vec.append(0)
        src_vectors[src] = np.array(vec)
    
    # Normalize
    all_vecs = np.array(list(src_vectors.values()))
    all_vecs = (all_vecs - all_vecs.mean(axis=0)) / (all_vecs.std(axis=0) + 1e-8)
    src_vecs_norm = {s: all_vecs[i] for i, s in enumerate(src_vectors.keys())}
    
    # Distance matrix
    src_list = list(src_vectors.keys())
    dist_mat = squareform(pdist(all_vecs, metric='euclidean'))
    
    fig, axes = plt.subplots(1, 2, figsize=(20, 8))
    
    # Heatmap
    im = axes[0].imshow(dist_mat, cmap='YlOrRd', aspect='auto')
    axes[0].set_xticks(range(len(src_list)))
    axes[0].set_yticks(range(len(src_list)))
    axes[0].set_xticklabels(src_list, rotation=45, ha='right', fontsize=7)
    axes[0].set_yticklabels(src_list, fontsize=7)
    axes[0].set_title('来源目录欧氏距离矩阵\n（基于墙体密度、箱密度、面积、宽高比）', fontsize=11)
    plt.colorbar(im, ax=axes[0], shrink=0.8, label='标准化欧氏距离')
    
    # Dendrogram
    Z = linkage(all_vecs, method='ward')
    dn = dendrogram(Z, labels=src_list, ax=axes[1], leaf_font_size=8, 
                    color_threshold=2.0, orientation='left')
    axes[1].set_title('来源目录层次聚类\n(Ward方法)', fontsize=11)
    axes[1].set_xlabel('距离')
    
    fig.suptitle('Layer 3-3: 来源目录特征相似度', fontsize=14, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L3_similarity.png')
    
    # 3-4: Per-source scatter summary (wall_rate vs box_rate per source)
    n_sources = len(SRC_ORDER)
    n_cols = 5
    n_rows = (n_sources + n_cols - 1) // n_cols
    fig, axes = plt.subplots(n_rows, n_cols, figsize=(n_cols*3, n_rows*3))
    axes_flat = axes.flatten()
    for i, src in enumerate(SRC_ORDER):
        ax = axes_flat[i]
        subset = [r for r in rows if r['source'] == src]
        x = [r['wall_rate'] for r in subset]
        y = [r['effective_box_rate'] for r in subset]
        ax.scatter(x, y, c=SRC_CMAP[src], alpha=0.5, s=10, edgecolors='none')
        ax.set_title(f'{src} (n={len(subset)})', fontsize=7)
        ax.set_xlabel('墙密度', fontsize=6)
        ax.set_ylabel('箱密度', fontsize=6)
        ax.tick_params(labelsize=6)
        # global reference
        ax.set_xlim(0, 0.85)
        ax.set_ylim(0, 0.4)
    # hide unused
    for j in range(i+1, len(axes_flat)):
        axes_flat[j].set_visible(False)
    fig.suptitle('Layer 3-4: 各来源墙密度 vs 箱密度散点图', fontsize=12, fontweight='bold', y=1.01)
    fig.tight_layout()
    save_and_close(fig, 'L3_per_source_scatter.png')

# ═══════════════════════════════════════════════════════
# MAIN
# ═══════════════════════════════════════════════════════
if __name__ == '__main__':
    print("\n=== Layer 1: 单变量分布 ===")
    layer1_distributions()
    print("\n=== Layer 2: 双变量关系 ===")
    layer2_bivariate()
    print("\n=== Layer 3: 来源目录对比 ===")
    layer3_source_comparison()
    print(f"\n全部图表已保存至: {OUT_DIR}")
