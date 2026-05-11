using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class WeightedPoolSO<TEntry, TRaw> : PoolBaseSO, IPoolAnalyzable
    where TEntry : PoolEntryBase
{
    public abstract IReadOnlyList<TEntry> Entries { get; }

    public virtual bool IsEmpty
    {
        get
        {
            var entries = Entries;
            return entries == null || entries.Count == 0;
        }
    }

    public virtual TRaw Roll(System.Random random, Func<TEntry, bool> predicate, Func<TEntry, TRaw> projector, TRaw fallback = default)
    {
        var entries = Entries;
        if (entries == null || entries.Count == 0)
            return fallback;

        int totalWeight = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!IsEntrySelectable(entry, predicate))
                continue;

            totalWeight += Mathf.Max(0, entry.weight);
        }

        if (totalWeight <= 0)
            return fallback;

        int pick = random != null ? random.Next(totalWeight) : UnityEngine.Random.Range(0, totalWeight);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!IsEntrySelectable(entry, predicate))
                continue;

            int weight = Mathf.Max(0, entry.weight);
            if (pick < weight)
                return projector != null ? projector(entry) : fallback;

            pick -= weight;
        }

        return fallback;
    }

    protected virtual bool IsEntrySelectable(TEntry entry, Func<TEntry, bool> predicate)
    {
        return enabled && entry != null && entry.enabled && (predicate == null || predicate(entry));
    }

    public virtual PoolAnalysisResult Analyze(PoolEvalContext context)
    {
        var result = new PoolAnalysisResult
        {
            poolId = poolId,
            displayName = GetResolvedDisplayName()
        };

        var entries = Entries;
        if (entries == null)
        {
            PoolAnalysisMath.Finalize(result);
            return result;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            bool selectable = enabled && entry != null && entry.enabled && GetAnalysisWeight(entry, context) > 0f;
            result.entries.Add(new PoolEntryAnalysis
            {
                id = i.ToString(),
                displayName = GetEntryDisplayName(entry, i),
                enabled = entry != null && entry.enabled,
                selectable = selectable,
                weight = entry != null ? GetAnalysisWeight(entry, context) : 0f,
                reason = BuildAnalysisReason(entry, selectable)
            });
        }

        PoolAnalysisMath.Finalize(result);
        return result;
    }

    protected virtual float GetAnalysisWeight(TEntry entry, PoolEvalContext context)
    {
        return entry != null ? Mathf.Max(0, entry.weight) : 0f;
    }

    protected virtual string GetEntryDisplayName(TEntry entry, int index)
    {
        return entry != null ? $"{typeof(TEntry).Name} {index}" : "<null>";
    }

    protected virtual string BuildAnalysisReason(TEntry entry, bool selectable)
    {
        if (!enabled)
            return "Pool disabled";
        if (entry == null)
            return "Null entry";
        if (!entry.enabled)
            return "Entry disabled";
        if (!selectable)
            return "Weight is zero";

        return "OK";
    }
}
