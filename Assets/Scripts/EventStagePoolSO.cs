using System;
using System.Collections.Generic;
using NekoGraph;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "EventStagePool", menuName = "BlockingKing/Pool/Event Stage Pool")]
public sealed class EventStagePoolSO : PoolBaseSO
{
    [Serializable]
    public sealed class Entry : PoolEntryBase
    {
        [TableColumnWidth(140)]
        public string stageId;

        [AssetsOnly]
        public TextAsset stagePack;
    }

    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 160)]
    public List<Entry> entries = new List<Entry>();

    public bool TryRollStagePack(System.Random random, out BasePackData pack, out string displayName)
    {
        pack = null;
        displayName = null;

        var candidates = new List<Entry>();
        foreach (var item in entries ?? new List<Entry>())
        {
            if (enabled && item != null && item.enabled && item.stagePack != null)
                candidates.Add(item);
        }

        if (candidates.Count == 0)
            return false;

        int index = random != null ? random.Next(candidates.Count) : UnityEngine.Random.Range(0, candidates.Count);
        var entry = candidates[index];

        if (entry?.stagePack == null || string.IsNullOrWhiteSpace(entry.stagePack.text))
            return false;

        try
        {
            pack = BasePackData.FromJson(entry.stagePack.text);
            displayName = !string.IsNullOrWhiteSpace(entry.stageId) ? entry.stageId : entry.stagePack.name;
            if (pack != null && string.IsNullOrWhiteSpace(pack.DisplayName))
                pack.DisplayName = displayName;

            return pack != null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[EventStagePoolSO] Failed to parse stage pack '{entry.stagePack.name}': {e.Message}");
            return false;
        }
    }

    private static string GetEntryDisplayName(Entry entry, int index)
    {
        if (entry == null)
            return $"Event {index}";

        if (!string.IsNullOrWhiteSpace(entry.stageId))
            return entry.stageId;

        return entry.stagePack != null ? entry.stagePack.name : $"Event {index}";
    }
}
