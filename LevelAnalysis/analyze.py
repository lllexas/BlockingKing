#!/usr/bin/env python3
"""Level features analysis for BlockingKing roguelike map stitching."""

import os, csv, sys
import numpy as np
from collections import Counter

ws = os.path.dirname(os.path.abspath(__file__))
csv_path = r"G:\ProjectOfGame\BlockingKing\Assets\Resources\Levels\level_features.csv"

with open(csv_path, 'r', encoding='utf-8-sig') as f:
    reader = csv.DictReader(f)
    rows = list(reader)

print(f"Total rows: {len(rows)}")
print(f"Columns: {list(rows[0].keys())}")

# Summarize by source directory
sources = Counter()
for r in rows:
    parts = r['path'].split('/')
    if 'Levels' in parts:
        idx = parts.index('Levels')
        if idx + 1 < len(parts):
            sources[parts[idx+1]] += 1

print("\nLevels per source directory:")
for k, v in sorted(sources.items()):
    print(f"  {k}: {v}")

# Basic stats
fields = ['width', 'height', 'area', 'wall_count', 'wall_rate', 'box_count', 'box_rate', 
          'target_count', 'box_on_target_count', 'effective_box_count', 'effective_box_rate']
print("\nNumerical summaries:")
for f in fields:
    vals = [float(r[f]) for r in rows]
    print(f"  {f:25s}: min={min(vals):10.4f}  max={max(vals):10.4f}  mean={np.mean(vals):10.4f}  median={np.median(vals):10.4f}  std={np.std(vals):10.4f}")
