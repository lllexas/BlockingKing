@echo off
cd /d %~dp0
python -c "
import csv, numpy as np
from collections import defaultdict
csv_path = r'G:\ProjectOfGame\BlockingKing\Assets\Resources\Levels\level_features.csv'
rows = []
with open(csv_path,'r',encoding='utf-8-sig') as f:
    for d in csv.DictReader(f):
        for k in ['width','height','area','wall_count','wall_rate','box_count','box_rate','target_count','box_on_target_count','effective_box_count','effective_box_rate']:
            d[k] = float(d[k])
        parts = d['path'].split('/')
        idx = parts.index('Levels') if 'Levels' in parts else -1
        d['source'] = parts[idx+1] if idx>=0 and idx+1<len(parts) else 'Unknown'
        d['emptiness'] = 1.0 - d['wall_rate']
        rows.append(d)
srcs = sorted(set(r['source'] for r in rows), key=lambda s: sum(1 for r in rows if r['source']==s), reverse=True)
print(f'{\"来源\":22s} {\"数量\":>5s} {\"均宽\":>6s} {\"均高\":>6s} {\"均面积\":>7s} {\"均墙密度\":>8s} {\"均箱密度\":>8s} {\"均有效箱数\":>8s}')
print('-'*85)
for src in srcs:
    sub = [r for r in rows if r['source']==src]
    n = len(sub)
    mw = np.mean([r['width'] for r in sub])
    mh = np.mean([r['height'] for r in sub])
    ma = np.mean([r['area'] for r in sub])
    mwr = np.mean([r['wall_rate'] for r in sub])
    mbr = np.mean([r['effective_box_rate'] for r in sub])
    meb = np.mean([r['effective_box_count'] for r in sub])
    print(f'{src:22s} {n:5d} {mw:6.1f} {mh:6.1f} {ma:7.1f} {mwr:8.3f} {mbr:8.4f} {meb:8.1f}')
"
