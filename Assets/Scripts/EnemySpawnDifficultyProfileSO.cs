using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemySpawnDifficultyProfile", menuName = "BlockingKing/Enemy Spawn Difficulty Profile")]
public class EnemySpawnDifficultyProfileSO : TableBaseSO, IPoolAnalyzable
{
    [Serializable]
    public sealed class Entry : PoolEntryBase
    {
        [TableColumnWidth(160)]
        public EntityBP enemyBP;
    }

    [Serializable]
    public sealed class Row
    {
        [Min(0)]
        public int roundIndex;

        public string label;

        [TableList(AlwaysExpanded = true, DrawScrollView = false)]
        public List<Entry> enemies = new List<Entry>();
    }

    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 180)]
    public List<Row> rows = new List<Row>();

    public EntityBP Roll(float overallDifficulty, int routeLayer, int routeLayerCount, int globalTick, Vector2Int spawnPosition, EntityBP fallback)
    {
        if (!enabled)
            return fallback;

        var entries = ResolveEntries(routeLayer);
        if (entries == null || entries.Count == 0)
            return fallback;

        float totalWeight = 0f;
        for (int i = 0; i < entries.Count; i++)
            totalWeight += GetWeight(entries[i]);

        if (totalWeight <= 0f)
            return fallback;

        var random = new System.Random(NextSeed(overallDifficulty, routeLayer, routeLayerCount, globalTick, spawnPosition));
        float pick = (float)random.NextDouble() * totalWeight;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            float weight = GetWeight(entry);
            if (weight <= 0f)
                continue;

            if (pick < weight)
                return entry.enemyBP != null ? entry.enemyBP : fallback;

            pick -= weight;
        }

        return fallback;
    }

    public PoolAnalysisResult Analyze(PoolEvalContext context)
    {
        var result = new PoolAnalysisResult
        {
            poolId = tableId,
            displayName = GetResolvedDisplayName()
        };

        var entries = ResolveEntries(context.routeLayer);
        if (entries == null)
        {
            PoolAnalysisMath.Finalize(result);
            return result;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            bool selectable = enabled && entry != null && entry.enabled && GetWeight(entry) > 0f;
            result.entries.Add(new PoolEntryAnalysis
            {
                id = i.ToString(),
                displayName = GetEntryDisplayName(entry, i),
                enabled = entry != null && entry.enabled,
                selectable = selectable,
                weight = entry != null ? GetWeight(entry) : 0f,
                reason = BuildAnalysisReason(entry, selectable)
            });
        }

        PoolAnalysisMath.Finalize(result);
        return result;
    }

    private IReadOnlyList<Entry> ResolveEntries(int roundIndex)
    {
        if (rows == null || rows.Count == 0)
            return null;

        Row best = null;
        int bestRound = int.MinValue;
        roundIndex = Mathf.Max(0, roundIndex);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null)
                continue;

            int rowRound = Mathf.Max(0, row.roundIndex);
            if (rowRound > roundIndex || rowRound < bestRound)
                continue;

            best = row;
            bestRound = rowRound;
        }

        return best?.enemies;
    }

    private static float GetWeight(Entry entry)
    {
        if (entry == null || !entry.enabled || entry.enemyBP == null)
            return 0f;

        return Mathf.Max(0, entry.weight);
    }

    private static string GetEntryDisplayName(Entry entry, int index)
    {
        if (entry?.enemyBP != null)
            return entry.enemyBP.name;

        return $"Enemy {index}";
    }

    private string BuildAnalysisReason(Entry entry, bool selectable)
    {
        if (!enabled)
            return "Table disabled";
        if (entry == null)
            return "Null entry";
        if (!entry.enabled)
            return "Entry disabled";
        if (entry.enemyBP == null)
            return "Enemy BP missing";
        if (!selectable)
            return "Zero weight";

        return "OK";
    }

    private static int NextSeed(float overallDifficulty, int routeLayer, int routeLayerCount, int globalTick, Vector2Int spawnPosition)
    {
        unchecked
        {
            int seed = 17;
            seed = seed * 31 + Mathf.RoundToInt(overallDifficulty * 1000f);
            seed = seed * 31 + routeLayer;
            seed = seed * 31 + routeLayerCount;
            seed = seed * 31 + globalTick;
            seed = seed * 31 + spawnPosition.x;
            seed = seed * 31 + spawnPosition.y;
            return seed;
        }
    }
}
