using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemySpawnTimingProfile", menuName = "BlockingKing/Stage/Enemy Spawn Timing Profile")]
public class EnemySpawnTimingProfileSO : TableBaseSO
{
    [Serializable]
    public sealed class Row
    {
        [Min(1)]
        public int roundIndex = 1;

        public string label;

        [Min(1), Tooltip("Ticks between successful spawns. This overrides Target.Enemy BP spawnInterval when the profile is configured.")]
        public int spawnInterval = 8;

        [Min(0), Tooltip("First spawn delay after the level starts. Use 0 to spawn as soon as the target is active.")]
        public int initialDelay = 8;

        [Min(0), Tooltip("Deterministic per-target random extra ticks added to interval and initial delay.")]
        public int jitter = 0;
    }

    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 180)]
    public List<Row> rows = new List<Row>();

    public bool TryResolve(int roundIndex, Vector2Int spawnPosition, int globalTick, out int interval, out int initialDelay)
    {
        interval = 0;
        initialDelay = 0;

        if (!enabled || rows == null || rows.Count == 0)
            return false;

        Row row = ResolveRow(roundIndex);
        if (row == null)
            return false;

        int extra = row.jitter > 0 ? StableJitter(roundIndex, spawnPosition, globalTick, row.jitter) : 0;
        interval = Mathf.Max(1, row.spawnInterval + extra);
        initialDelay = Mathf.Max(0, row.initialDelay + extra);
        return true;
    }

    public int ResolveInterval(int fallback, int roundIndex, Vector2Int spawnPosition, int globalTick)
    {
        return TryResolve(roundIndex, spawnPosition, globalTick, out int interval, out _)
            ? interval
            : Mathf.Max(1, fallback);
    }

    public int ResolveInitialDelay(int fallback, int roundIndex, Vector2Int spawnPosition, int globalTick)
    {
        return TryResolve(roundIndex, spawnPosition, globalTick, out _, out int initialDelay)
            ? initialDelay
            : Mathf.Max(0, fallback);
    }

    private Row ResolveRow(int roundIndex)
    {
        Row best = null;
        int bestRound = int.MinValue;
        roundIndex = Mathf.Max(1, roundIndex);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null)
                continue;

            int rowRound = Mathf.Max(1, row.roundIndex);
            if (rowRound > roundIndex || rowRound < bestRound)
                continue;

            best = row;
            bestRound = rowRound;
        }

        return best;
    }

    private static int StableJitter(int roundIndex, Vector2Int spawnPosition, int globalTick, int maxInclusive)
    {
        unchecked
        {
            int seed = 23;
            seed = seed * 31 + roundIndex;
            seed = seed * 31 + spawnPosition.x;
            seed = seed * 31 + spawnPosition.y;
            seed = seed * 31 + globalTick;
            return Mathf.Abs(seed) % (maxInclusive + 1);
        }
    }
}
