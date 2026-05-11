using UnityEngine;

public abstract class DynamicWeightedPoolSO<TEntry, TRaw> : WeightedPoolSO<TEntry, TRaw>
    where TEntry : PoolEntryBase
{
    protected override float GetAnalysisWeight(TEntry entry, PoolEvalContext context)
    {
        return EvaluateWeight(entry, context);
    }

    protected abstract float EvaluateWeight(TEntry entry, PoolEvalContext context);

    protected TRaw RollDynamic(PoolEvalContext context, System.Random random, System.Func<TEntry, TRaw> projector, TRaw fallback = default)
    {
        var entries = Entries;
        if (!enabled || entries == null || entries.Count == 0)
            return fallback;

        float totalWeight = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null || !entry.enabled)
                continue;

            totalWeight += Mathf.Max(0f, EvaluateWeight(entry, context));
        }

        if (totalWeight <= 0f)
            return fallback;

        float pick = (float)(random != null ? random.NextDouble() : UnityEngine.Random.value) * totalWeight;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry == null || !entry.enabled)
                continue;

            float weight = Mathf.Max(0f, EvaluateWeight(entry, context));
            if (pick < weight)
                return projector != null ? projector(entry) : fallback;

            pick -= weight;
        }

        return fallback;
    }

    protected static float EvaluateLinearWeight(float baseWeight, float add, float multiplier = 1f)
    {
        return Mathf.Max(0f, baseWeight + add) * Mathf.Max(0f, multiplier);
    }
}
