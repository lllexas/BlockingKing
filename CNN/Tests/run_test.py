"""
地图生成测试 — 三段管线: BoxPlacer → GoalPlacer → WallFill

用法:
    python Tests/run_test.py --w 12 --h 10 --n 2 --batch 5
    python Tests/run_test.py --w 10 --h 10 --n 1 --batch 3 --seed 42 --temp 0.5
"""

import sys, os, argparse, time
_SCRIPT = os.path.dirname(os.path.abspath(__file__))
from generator import Generator, ascii_map


def main():
    parser = argparse.ArgumentParser(description="三段管线地图生成器")
    p = parser.add_argument
    p('--w', type=int, default=12, help='宽度 (5~20)')
    p('--h', type=int, default=10, help='高度 (5~20)')
    p('--n', type=int, default=2, help='箱点对数 (1~5)')
    p('--batch', type=int, default=5, help='生成张数')
    p('--seed', type=int, default=None, help='随机种子')
    p('--temp', type=float, default=1.0, help='采样温度 (<1贪心, >1随机)')
    p('--box-placer', type=str, default=f'{_SCRIPT}/../MapBoxPlacer/weights/epoch30/')
    p('--goal-placer', type=str, default=f'{_SCRIPT}/../MapGoalPlacer/weights/epoch30/')
    p('--wall-fill', type=str, default=f'{_SCRIPT}/../MapWallFill/weights/epoch30/')
    args = parser.parse_args()

    if not (5 <= args.w <= 20 and 5 <= args.h <= 20):
        print("⚠ W,H 需在 5~20"); return
    if not (1 <= args.n <= 5):
        print("⚠ N 需在 1~5"); return

    print(f"三段管线地图生成")
    print(f"  尺寸: {args.w}×{args.h}  N: {args.n}  批次: {args.batch}  temp: {args.temp}")
    print(f"  BoxPlacer:  {args.box_placer}")
    print(f"  GoalPlacer: {args.goal_placer}")
    print(f"  WallFill:   {args.wall_fill}")
    print("-" * 60)

    t0 = time.time()
    gen = Generator(args.box_placer, args.goal_placer, args.wall_fill)
    results = gen.generate(
        args.w, args.h, args.n,
        batch=args.batch, seed=args.seed, temperature=args.temp,
    )
    dt = time.time() - t0

    for i, r in enumerate(results):
        print(f"\n── 地图 {i+1} ──")
        print(ascii_map(r['full']))
        wp = r['wall'].sum() / (args.w * args.h) * 100
        print(f"  墙:{wp:.0f}%  箱:{r['box_count']}  目标:{r['goal_count']}")

    print(f"\n{'='*60}")
    print(f"总耗时: {dt*1000:.0f}ms ({dt/len(results)*1000:.0f}ms/张)")


if __name__ == '__main__':
    main()
