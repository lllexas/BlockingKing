using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LevelCollageSourceDatabase", menuName = "BlockingKing/Level Collage Source Database")]
public class LevelCollageSourceDatabase : ContentPoolSO<LevelCollageSourceEntry, LevelData>
{
    public List<LevelCollageSourceEntry> entries = new();

    public override IReadOnlyList<LevelCollageSourceEntry> Entries => entries;

    public List<LevelCollageSourceEntry> GetEnabledEntries()
    {
        var result = new List<LevelCollageSourceEntry>();
        foreach (var entry in entries)
        {
            if (entry?.level != null && entry.enabled)
                result.Add(entry);
        }

        return result;
    }

    protected override string GetEntryDisplayName(LevelCollageSourceEntry entry, int index)
    {
        if (entry?.level != null)
            return entry.level.name;

        return base.GetEntryDisplayName(entry, index);
    }

    protected override string BuildAnalysisReason(LevelCollageSourceEntry entry, bool selectable)
    {
        if (!enabled)
            return "Pool disabled";
        if (entry == null)
            return "Null entry";
        if (!entry.enabled)
            return "Entry disabled";
        if (entry.level == null)
            return "Level missing";
        if (!selectable)
            return "Not selectable";

        return "OK";
    }
}

[Serializable]
public sealed class LevelCollageSourceEntry : PoolEntryBase
{
    public LevelData level;
    public string sourcePath;
    public string sourceGroup;
    public int width;
    public int height;
    public int area;
    public int wallCount;
    public float wallRate;
    public int boxCount;
    public float boxRate;
    public int targetCount;
    public int boxOnTargetCount;
    public int effectiveBoxCount;
    public float effectiveBoxRate;
    public int manualWeight = 1;
}
