using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class ContentPoolSO<TEntry, TRaw> : WeightedPoolSO<TEntry, TRaw>
    where TEntry : PoolEntryBase
{
    protected override float GetAnalysisWeight(TEntry entry, PoolEvalContext context)
    {
        return entry != null && entry.enabled ? 1f : 0f;
    }

    public override TRaw Roll(System.Random random, Func<TEntry, bool> predicate, Func<TEntry, TRaw> projector, TRaw fallback = default)
    {
        var entries = Entries;
        if (entries == null || entries.Count == 0)
            return fallback;

        var candidates = new List<TEntry>();
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (enabled && entry != null && entry.enabled && (predicate == null || predicate(entry)))
                candidates.Add(entry);
        }

        if (candidates.Count == 0)
            return fallback;

        int index = random != null ? random.Next(candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
        return projector != null ? projector(candidates[index]) : fallback;
    }
}
