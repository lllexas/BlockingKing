using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemySpawnDifficultyProfile", menuName = "BlockingKing/Enemy Spawn Difficulty Profile")]
public class EnemySpawnDifficultyProfileSO : TableBaseSO, IPoolAnalyzable
{
    [Serializable]
    public sealed class Entry : PoolEntryBase
    {
        public EntityBP enemyBP;

        [Range(0f, 1f)]
        public float minProgress;

        [Range(0f, 1f)]
        public float maxProgress = 1f;

        [Min(0f)]
        public float minDifficulty;

        [Min(0f)]
        public float baseWeight = 1f;

        public float difficultyWeightScale;
        public float progressWeightScale;
    }

    public List<Entry> entries = new List<Entry>();

    public EntityBP Roll(float overallDifficulty, int routeLayer, int routeLayerCount, int globalTick, Vector2Int spawnPosition, EntityBP fallback)
    {
        if (!enabled || entries == null || entries.Count == 0)
            return fallback;

        float progress = routeLayerCount > 1
            ? Mathf.Clamp01(routeLayer / (float)(routeLayerCount - 1))
            : 0f;

        float totalWeight = 0f;
        for (int i = 0; i < entries.Count; i++)
            totalWeight += GetWeight(entries[i], overallDifficulty, progress);

        if (totalWeight <= 0f)
            return fallback;

        var random = new System.Random(NextSeed(overallDifficulty, routeLayer, routeLayerCount, globalTick, spawnPosition));
        float pick = (float)random.NextDouble() * totalWeight;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            float weight = GetWeight(entry, overallDifficulty, progress);
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

        if (entries == null)
        {
            PoolAnalysisMath.Finalize(result);
            return result;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            bool selectable = enabled && entry != null && entry.enabled && GetWeight(entry, context.difficulty, context.progress) > 0f;
            result.entries.Add(new PoolEntryAnalysis
            {
                id = i.ToString(),
                displayName = GetEntryDisplayName(entry, i),
                enabled = entry != null && entry.enabled,
                selectable = selectable,
                weight = entry != null ? GetWeight(entry, context.difficulty, context.progress) : 0f,
                reason = BuildAnalysisReason(entry, selectable)
            });
        }

        PoolAnalysisMath.Finalize(result);
        return result;
    }

    private static float GetWeight(Entry entry, float overallDifficulty, float progress)
    {
        if (entry == null || !entry.enabled || entry.enemyBP == null)
            return 0f;

        float minProgress = Mathf.Clamp01(Mathf.Min(entry.minProgress, entry.maxProgress));
        float maxProgress = Mathf.Clamp01(Mathf.Max(entry.minProgress, entry.maxProgress));
        if (progress < minProgress || progress > maxProgress)
            return 0f;

        if (overallDifficulty < entry.minDifficulty)
            return 0f;

        float difficultyDelta = Mathf.Max(0f, overallDifficulty - entry.minDifficulty);
        return Mathf.Max(0f, entry.baseWeight +
                              entry.difficultyWeightScale * difficultyDelta +
                              entry.progressWeightScale * progress);
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
            return "Out of progress/difficulty range or zero weight";

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
