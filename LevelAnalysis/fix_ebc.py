"""Fix effective_box_count histogram — use log bins."""
import csv, os, numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

plt.rcParams['font.sans-serif'] = ['Microsoft YaHei', 'SimHei', 'DejaVu Sans']
plt.rcParams['axes.unicode_minus'] = False
plt.rcParams['figure.dpi'] = 120
plt.rcParams['savefig.dpi'] = 150
plt.rcParams['savefig.bbox'] = 'tight'

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'output')
os.makedirs(OUT_DIR, exist_ok=True)

CSV_PATH = r"G:\ProjectOfGame\BlockingKing\Assets\Resources\Levels\level_features.csv"
ebc = []
with open(CSV_PATH, 'r', encoding='utf-8-sig') as f:
    for d in csv.DictReader(f):
        ebc.append(float(d['effective_box_count']))

ebc = np.array(ebc)

fig, axes = plt.subplots(1, 2, figsize=(14, 5.5))

# Left: log-scale histogram with log-spaced bins
log_bins = np.logspace(np.log10(1), np.log10(max(ebc)), 60)
axes[0].hist(ebc, bins=log_bins, color='teal', edgecolor='white', alpha=0.85)
axes[0].set_xscale('log')
axes[0].axvline(np.median(ebc), color='red', linestyle='--', linewidth=1.5,
                label=f'中位数 = {np.median(ebc):.0f}')
axes[0].set_xlabel('有效箱子数 (log scale)')
axes[0].set_ylabel('关卡数')
axes[0].set_title('有效箱子数分布（对数轴）')
axes[0].legend(fontsize=9)
axes[0].grid(axis='y', alpha=0.3)

# Right: focus on 1-50 range (where 90%+ of data lives)
p95 = np.percentile(ebc, 95)
axes[1].hist(ebc, bins=np.arange(0.5, min(p95 + 5, 55), 1), color='teal', edgecolor='white', alpha=0.85)
axes[1].axvline(np.median(ebc), color='red', linestyle='--', linewidth=1.5,
                label=f'中位数 = {np.median(ebc):.0f}')
axes[1].axvline(p95, color='gray', linestyle=':', linewidth=1,
                label=f'P95 = {p95:.0f}')
pct_above = 100 * np.mean(ebc > 50)
axes[1].set_xlabel('有效箱子数')
axes[1].set_ylabel('关卡数')
axes[1].set_title(f'有效箱子数分布（0-{min(p95+5,55):.0f} 范围，P95={p95:.0f}）\n'
                  f'超过50箱占比: {pct_above:.1f}%')
axes[1].legend(fontsize=9)
axes[1].grid(axis='y', alpha=0.3)

fig.suptitle('有效箱子数分布（修正版）', fontsize=13, fontweight='bold', y=1.01)
fig.tight_layout()
out_path = os.path.join(OUT_DIR, 'L1_effective_box_count_fixed.png')
fig.savefig(out_path)
plt.close(fig)
print(f"Saved: {out_path}")
print(f"Stats: min={min(ebc):.0f}  P25={np.percentile(ebc,25):.0f}  median={np.median(ebc):.0f}  P75={np.percentile(ebc,75):.0f}  P95={p95:.0f}  max={max(ebc):.0f}")
print(f"  >50 boxes: {pct_above:.1f}%  >100 boxes: {100*np.mean(ebc>100):.1f}%")
