using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "StageTypePool", menuName = "BlockingKing/Pool/Stage Type Pool")]
public class StageTypePoolSO : DynamicWeightedPoolSO<StageTypePoolSO.Entry, StagePoolSO.StageEntryKind>
{
    [Serializable]
    public sealed class Entry : PoolEntryBase
    {
        [TableColumnWidth(96)]
        public StagePoolSO.StageEntryKind kind = StagePoolSO.StageEntryKind.ClassicLevel;

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

    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<Entry> entries = new List<Entry>();

    public override IReadOnlyList<Entry> Entries => entries;

    public bool TryRoll(PoolEvalContext context, System.Random random, out StagePoolSO.StageEntryKind kind)
    {
        kind = default;
        var analysis = Analyze(context);
        if (analysis.totalWeight <= 0f)
            return false;

        kind = RollDynamic(context, random, entry => entry.kind);
        return true;
    }

    protected override float EvaluateWeight(Entry entry, PoolEvalContext context)
    {
        if (entry == null || !entry.enabled)
            return 0f;

        float minProgress = Mathf.Clamp01(Mathf.Min(entry.minProgress, entry.maxProgress));
        float maxProgress = Mathf.Clamp01(Mathf.Max(entry.minProgress, entry.maxProgress));
        if (context.progress < minProgress || context.progress > maxProgress)
            return 0f;

        if (context.difficulty < entry.minDifficulty)
            return 0f;

        float difficultyDelta = Mathf.Max(0f, context.difficulty - entry.minDifficulty);
        return EvaluateLinearWeight(
            entry.baseWeight,
            entry.difficultyWeightScale * difficultyDelta + entry.progressWeightScale * context.progress);
    }

    protected override string GetEntryDisplayName(Entry entry, int index)
    {
        return entry != null ? entry.kind.ToString() : base.GetEntryDisplayName(entry, index);
    }
}
