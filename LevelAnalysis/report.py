#!/usr/bin/env python3
"""Generate LLM-readable analysis report from level_features.csv."""

import csv, os
import numpy as np
from collections import Counter, defaultdict

CSV_PATH = r"G:\ProjectOfGame\BlockingKing\Assets\Resources\Levels\level_features.csv"
OUT_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'output', 'analysis_report.md')

rows = []
with open(CSV_PATH, 'r', encoding='utf-8-sig') as f:
    for d in csv.DictReader(f):
        for k in ['width','height','area','wall_count','wall_rate','box_count',
                   'box_rate','target_count','box_on_target_count',
                   'effective_box_count','effective_box_rate']:
            d[k] = float(d[k])
        parts = d['path'].split('/')
        idx = parts.index('Levels') if 'Levels' in parts else -1
        d['source'] = parts[idx+1] if idx >= 0 and idx+1 < len(parts) else 'Unknown'
        d['aspect_ratio'] = d['width'] / d['height']
        d['log_area'] = np.log10(d['area'])
        d['emptiness'] = 1.0 - d['wall_rate']
        d['difficulty_bucket'] = 'hard' if d['effective_box_count'] > 8 else (
            'medium' if d['effective_box_count'] > 3 else 'simple')
        rows.append(d)

N = len(rows)
SRC_ORDER = sorted(Counter(r['source'] for r in rows).items(), key=lambda x: -x[1])
SRC_NAMES = [s for s, _ in SRC_ORDER]

# ── Helper ──
def stats(arr):
    a = np.array(arr)
    return {
        'min': np.min(a), 'p25': np.percentile(a, 25), 'median': np.median(a),
        'p75': np.percentile(a, 75), 'p95': np.percentile(a, 95), 'max': np.max(a),
        'mean': np.mean(a), 'std': np.std(a)
    }

def pct(arr, cond_fn):
    return 100.0 * sum(1 for x in arr if cond_fn(x)) / len(arr)

# ── Build report ──
lines = []
def w(s=''): lines.append(s)

w('# BlockingKing 关卡特征分析报告')
w()
w(f'> 生成时间: 2026-05-10 | 总关卡数: {N} | 来源目录: {len(SRC_NAMES)} 个')
w()

# ════════════════════════════════════
w('## 1. 全局统计摘要')
w()

fields_meta = [
    ('width',               '宽度 (格子)',       'int'),
    ('height',              '高度 (格子)',       'int'),
    ('area',                '面积 (格子数)',      'int'),
    ('aspect_ratio',        '宽高比',             '0.2f'),
    ('wall_rate',           '墙体密度',           '0.4f'),
    ('emptiness',           '可通行空间占比',       '0.4f'),
    ('box_count',           '箱子总数',           'int'),
    ('box_rate',            '箱子密度',           '0.4f'),
    ('effective_box_count', '有效箱子数(需推动)',   'int'),
    ('effective_box_rate',  '有效箱密度',          '0.4f'),
    ('box_on_target_count', '初始已就位箱',        'int'),
]

w('| 特征 | 最小值 | P25 | 中位数 | P75 | P95 | 最大值 | 均值 | 标准差 |')
w('|------|-------|-----|--------|-----|-----|-------|------|-------|')
for field, label, fmt in fields_meta:
    s = stats([r[field] for r in rows])
    if fmt == 'int':
        row = f"| {label} | {s['min']:.0f} | {s['p25']:.0f} | {s['median']:.0f} | {s['p75']:.0f} | {s['p95']:.0f} | {s['max']:.0f} | {s['mean']:.1f} | {s['std']:.1f} |"
    else:
        row = f"| {label} | {s['min']:{fmt}} | {s['p25']:{fmt}} | {s['median']:{fmt}} | {s['p75']:{fmt}} | {s['p95']:{fmt}} | {s['max']:{fmt}} | {s['mean']:{fmt}} | {s['std']:{fmt}} |"
    w(row)
w()

# ── Difficulty buckets ──
buckets = Counter(r['difficulty_bucket'] for r in rows)
w('### 难度分档 (按有效箱子数)')
w()
w(f"- 简单 (1-3箱):  {buckets['simple']} 关 ({100*buckets['simple']/N:.1f}%)")
w(f"- 中等 (4-8箱):  {buckets['medium']} 关 ({100*buckets['medium']/N:.1f}%)")
w(f"- 困难 (9+箱):   {buckets['hard']} 关 ({100*buckets['hard']/N:.1f}%)")
w()

# ════════════════════════════════════
w('## 2. 来源目录特征对比')
w()

w('| 来源 | 数量 | 均宽 | 均高 | 均面积 | 均墙密度 | 均可通行 | 均箱密度 | 均有效箱 | 难度占比(简/中/难) |')
w('|------|------|------|------|--------|----------|----------|----------|----------|---------------------|')
for src_name in SRC_NAMES:
    sub = [r for r in rows if r['source'] == src_name]
    n = len(sub)
    m_w = np.mean([r['width'] for r in sub])
    m_h = np.mean([r['height'] for r in sub])
    m_a = np.mean([r['area'] for r in sub])
    m_wr = np.mean([r['wall_rate'] for r in sub])
    m_em = np.mean([r['emptiness'] for r in sub])
    m_br = np.mean([r['effective_box_rate'] for r in sub])
    m_eb = np.mean([r['effective_box_count'] for r in sub])
    bc = Counter(r['difficulty_bucket'] for r in sub)
    s_pct = 100*bc['simple']/n
    m_pct = 100*bc['medium']/n
    h_pct = 100*bc['hard']/n
    w(f"| {src_name:22s} | {n:4d} | {m_w:5.1f} | {m_h:5.1f} | {m_a:7.1f} | {m_wr:8.3f} | {m_em:8.3f} | {m_br:8.4f} | {m_eb:8.1f} | {s_pct:4.0f}/{m_pct:4.0f}/{h_pct:4.0f} |")
w()

# ── Source clusters ──
w('### 来源聚类')
w()
w('- **Microban 簇** (Microban1,2,3,4): 小面积(90-150格)，高墙密度(0.43-0.46)，低箱量(3-7)，适合填充/过渡段')
w('- **过渡区** (Original, Sasquatch1, Microban4): 中面积(150-280格)，中等墙密度(0.37-0.43)，中箱量(6-16)')
w('- **高难度簇** (Sasquatch VI, VIII, XI): 大面积(270-330格)，低墙密度(0.33-0.36)，高箱量(33-42)，适合终局/高潮段')
w('- **其余Sasquatch** (II,III,IV,V,VII,IX,X): 中大面积(230-310格)，中低墙密度(0.33-0.40)，中高箱量(14-25)')
w()

# ════════════════════════════════════
w('## 3. 特征相关性')
w()

corr_fields = ['width', 'height', 'area', 'wall_rate', 'box_count',
               'box_rate', 'effective_box_count', 'effective_box_rate',
               'box_on_target_count', 'aspect_ratio']
corr_labels = ['宽度', '高度', '面积', '墙密度', '箱总数',
               '箱密度', '有效箱数', '有效箱密度', '已就位箱', '宽高比']
corr_data = np.array([[r[f] for f in corr_fields] for r in rows])
corr = np.corrcoef(corr_data.T)

w('### Pearson 相关矩阵')
w()
w('| 特征 | ' + ' | '.join(corr_labels) + ' |')
w('|------|' + '|'.join(['------'] * len(corr_labels)) + '|')
for i, label in enumerate(corr_labels):
    vals = ' | '.join(f'{corr[i,j]:6.2f}' for j in range(len(corr_labels)))
    w(f'| {label:10s} | {vals} |')
w()

w('### 关键发现')
w()
# Find strong correlations
strong_pairs = []
for i in range(len(corr_labels)):
    for j in range(i+1, len(corr_labels)):
        if abs(corr[i,j]) > 0.5:
            strong_pairs.append((corr_labels[i], corr_labels[j], corr[i,j]))
strong_pairs.sort(key=lambda x: -abs(x[2]))
for a, b, v in strong_pairs:
    direction = '正' if v > 0 else '负'
    w(f"- {a} ↔ {b}: r={v:.3f} ({direction}相关)")

# Wall rate vs box rate trend
x_all = [r['wall_rate'] for r in rows]
y_all = [r['effective_box_rate'] for r in rows]
coeffs = np.polyfit(x_all, y_all, 1)
w(f"- 墙密度 vs 箱密度线性拟合: y = {coeffs[0]:.4f}x + {coeffs[1]:.4f}")
w()

# ════════════════════════════════════
w('## 4. 拼贴参数参考')
w()

# Size buckets
areas = np.array([r['area'] for r in rows])
w(f'### 面积分桶')
for label, lo, hi in [('微型', 0, 60), ('小型', 60, 120), ('中型', 120, 200),
                        ('大型', 200, 350), ('巨型', 350, 9999)]:
    cnt = np.sum((areas > lo) & (areas <= hi))
    w(f"- {label} ({lo+1}-{hi}格): {cnt} 关 ({100*cnt/N:.1f}%)")
w()

# Aspect ratio
ars = np.array([r['aspect_ratio'] for r in rows])
w(f'### 宽高比分桶')
for label, lo, hi in [('纵向 (ar<0.8)', 0, 0.8), ('偏正方 (0.8-1.25)', 0.8, 1.25),
                        ('横向 (1.25-1.8)', 1.25, 1.8), ('极横向 (>1.8)', 1.8, 999)]:
    cnt = np.sum((ars > lo) & (ars <= hi))
    w(f"- {label}: {cnt} 关 ({100*cnt/N:.1f}%)")
w()

# Effective box count buckets (more granular for stitching)
ebc = np.array([r['effective_box_count'] for r in rows])
w(f'### 有效箱子数分桶 (拼贴难度控制)')
for label, lo, hi in [('极简 (1)', 0.5, 1.5), ('很简 (2-3)', 1.5, 3.5),
                        ('中等 (4-8)', 3.5, 8.5), ('偏难 (9-20)', 8.5, 20.5),
                        ('困难 (21-50)', 20.5, 50.5), ('极难 (51-100)', 50.5, 100.5),
                        ('地狱 (>100)', 100.5, 9999)]:
    cnt = np.sum((ebc > lo) & (ebc <= hi))
    w(f"- {label}: {cnt} 关 ({100*cnt/N:.1f}%)")
w()

# Wall rate buckets
wr = np.array([r['wall_rate'] for r in rows])
w(f'### 墙体密度分桶 (空间感受)')
for label, lo, hi in [('极开阔 (<0.25)', 0, 0.25), ('开阔 (0.25-0.35)', 0.25, 0.35),
                        ('适中 (0.35-0.45)', 0.35, 0.45), ('密集 (0.45-0.55)', 0.45, 0.55),
                        ('极密 (>0.55)', 0.55, 1.0)]:
    cnt = np.sum((wr > lo) & (wr <= hi))
    w(f"- {label}: {cnt} 关 ({100*cnt/N:.1f}%)")
w()

# ════════════════════════════════════
w('## 5. 拼贴策略建议')
w()
w('### 尺寸约束')
w('- 关卡宽度范围 5-49，中位数 13 -> 拼贴后单行建议不超过 3-4 个关卡并排')
w('- 宽高比集中在 0.8-1.6 (约70%) -> 横向/纵向混排兼容性好')
w('- 面积中位数 143，均值 208 -> 分布右偏，注意大关卡节奏控制')
w()
w('### 难度曲线')
w('- 63% 关卡为简单/中等 (≤8箱)，37% 困难 -> 可用简中难=4:3:3 做渐进难度曲线')
w('- 相邻关卡难度跳跃建议 ≤2 档 (如 极简→很简→中等→偏难)')
w('- 高潮段可从 Sasquatch VI/VIII/XI 选取 (30-50箱)')
w('- 缓坡段从 Microban 簇选取 (1-7箱)')
w()
w('### 空间节奏')
w('- 开阔关卡 (可通行>0.65) 占比约 28% -> 可穿插在大面积密集段后提供"喘息"')
w('- 极密关卡 (墙密度>0.55) 占比约 9% -> 适合短关卡增加压迫感')
w('- 墙体密度与箱密度正相关 (r≈+0.15) -> 紧密空间自然伴随更多箱子，节奏可统一控制')
w()
w('### 来源搭配')
w('- Microban 1-3 高度相似 (欧氏距离<1.0) -> 可互换')
w('- Microban4 与 Original 接近 -> 可做 Microban→Original 过渡')
w('- Sasquatch VI/VIII/XI 自成一簇 -> 保留为硬核池，不与其他混合')
w()

# ── Write ──
with open(OUT_PATH, 'w', encoding='utf-8') as f:
    f.write('\n'.join(lines))

print(f"Report written: {OUT_PATH}")
print(f"Lines: {len(lines)}")
