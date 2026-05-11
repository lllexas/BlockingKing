using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct PoolEvalContext
{
    [Range(0f, 1f)]
    public float progress;

    [Min(0f)]
    public float difficulty;

    public int routeLayer;
    public int routeLayerCount;
    public int seed;

    public static PoolEvalContext Default => new PoolEvalContext
    {
        progress = 0f,
        difficulty = 1f,
        routeLayer = 0,
        routeLayerCount = 1,
        seed = 0
    };
}

[Serializable]
public sealed class PoolEntryAnalysis
{
    public string id;
    public string displayName;
    public bool enabled;
    public bool selectable;
    public float weight;
    public float probability;
    public string reason;
}

[Serializable]
public sealed class PoolAnalysisResult
{
    public string poolId;
    public string displayName;
    public int totalEntries;
    public int selectableEntries;
    public float totalWeight;
    public float entropy;
    public float normalizedEntropy;
    public float meanWeight;
    public float standardDeviation;
    public readonly List<PoolEntryAnalysis> entries = new List<PoolEntryAnalysis>();
}

public interface IPoolAnalyzable
{
    PoolAnalysisResult Analyze(PoolEvalContext context);
}

public static class PoolAnalysisMath
{
    public static void Finalize(PoolAnalysisResult result)
    {
        if (result == null)
            return;

        result.totalEntries = result.entries.Count;
        result.selectableEntries = 0;
        result.totalWeight = 0f;

        for (int i = 0; i < result.entries.Count; i++)
        {
            var entry = result.entries[i];
            if (entry == null || !entry.selectable || entry.weight <= 0f)
                continue;

            result.selectableEntries++;
            result.totalWeight += entry.weight;
        }

        float meanSum = 0f;
        float varianceSum = 0f;
        result.entropy = 0f;

        for (int i = 0; i < result.entries.Count; i++)
        {
            var entry = result.entries[i];
            if (entry == null || !entry.selectable || entry.weight <= 0f || result.totalWeight <= 0f)
            {
                if (entry != null)
                    entry.probability = 0f;
                continue;
            }

            entry.probability = entry.weight / result.totalWeight;
            result.entropy -= entry.probability * Mathf.Log(entry.probability, 2f);
            meanSum += entry.weight;
        }

        result.meanWeight = result.selectableEntries > 0 ? meanSum / result.selectableEntries : 0f;
        for (int i = 0; i < result.entries.Count; i++)
        {
            var entry = result.entries[i];
            if (entry == null || !entry.selectable || entry.weight <= 0f)
                continue;

            float delta = entry.weight - result.meanWeight;
            varianceSum += delta * delta;
        }

        result.standardDeviation = result.selectableEntries > 0
            ? Mathf.Sqrt(varianceSum / result.selectableEntries)
            : 0f;

        result.normalizedEntropy = result.selectableEntries > 1
            ? result.entropy / Mathf.Log(result.selectableEntries, 2f)
            : 0f;
    }
}
