using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EnemySpawnDifficultyProfile", menuName = "BlockingKing/Enemy Spawn Difficulty Profile")]
public class EnemySpawnDifficultyProfileSO : ScriptableObject
{
    [Serializable]
    public sealed class Entry
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
        if (entries == null || entries.Count == 0)
            return fallback;

        float progress = routeLayerCount > 1
            ? Mathf.Clamp01(routeLayer / (float)(routeLayerCount - 1))
            : 0f;

        float totalWeight = 0f;
        for (int i = 0; i < entries.Count; i++)
            totalWeight += GetWeight(entries[i], overallDifficulty, progress);

        if (totalWeight <= 0f)
            return fallback;

        float pick = Next01(overallDifficulty, routeLayer, routeLayerCount, globalTick, spawnPosition) * totalWeight;
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

    private static float GetWeight(Entry entry, float overallDifficulty, float progress)
    {
        if (entry == null || entry.enemyBP == null)
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

    private static float Next01(float overallDifficulty, int routeLayer, int routeLayerCount, int globalTick, Vector2Int spawnPosition)
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
            var random = new System.Random(seed);
            return (float)random.NextDouble();
        }
    }
}
