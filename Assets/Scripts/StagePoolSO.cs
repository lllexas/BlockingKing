using System;
using System.Collections.Generic;
using NekoGraph;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "StagePool", menuName = "BlockingKing/Pool/Stage Pool")]
public class StagePoolSO : ContentPoolSO<StagePoolSO.Entry, RunRouteStageSource>
{
    public enum StageEntryKind
    {
        ClassicLevel,
        Encounter,
        Shop,
        Escort
    }

    [Serializable]
    public sealed class Entry : PoolEntryBase
    {
        [TableColumnWidth(86)]
        public StageEntryKind kind = StageEntryKind.ClassicLevel;

        [TableColumnWidth(140)]
        public string stageId;

        [TableColumnWidth(76)]
        public string stageType;

        [AssetsOnly]
        [ShowIf(nameof(IsClassicLevel))]
        public LevelData levelData;

        [AssetsOnly]
        [ShowIf(nameof(IsEncounter))]
        public TextAsset stagePack;

        [AssetsOnly]
        [ShowIf(nameof(IsShop))]
        public ShopSO shop;

        [AssetsOnly]
        [ShowIf(nameof(IsEscort))]
        public LevelCollageGenerationSettings collageGenerationSettings;

        private bool IsClassicLevel => kind == StageEntryKind.ClassicLevel;
        private bool IsEncounter => kind == StageEntryKind.Encounter;
        private bool IsShop => kind == StageEntryKind.Shop;
        private bool IsEscort => kind == StageEntryKind.Escort;
    }

    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 160)]
    public List<Entry> entries = new List<Entry>();

    public override IReadOnlyList<Entry> Entries => entries;

    public List<RunRouteStageSource> BuildRouteStageSources(LevelCollageGenerationSettings defaultCollageGenerationSettings)
    {
        return BuildRouteStageSources(defaultCollageGenerationSettings, null);
    }

    public List<RunRouteStageSource> BuildRouteStageSources(
        LevelCollageGenerationSettings defaultCollageGenerationSettings,
        StageEntryKind? kindFilter)
    {
        var result = new List<RunRouteStageSource>();
        foreach (var entry in entries ?? new List<Entry>())
        {
            if (kindFilter.HasValue && entry != null && entry.kind != kindFilter.Value)
                continue;

            var source = ToRouteStageSource(entry, defaultCollageGenerationSettings);
            if (source != null)
                result.Add(source);
        }

        return result;
    }

    public bool TryRollSource(
        StageEntryKind kind,
        LevelCollageGenerationSettings defaultCollageGenerationSettings,
        System.Random random,
        out RunRouteStageSource source)
    {
        source = Roll(
            random,
            entry => entry != null && entry.kind == kind && ToRouteStageSource(entry, defaultCollageGenerationSettings) != null,
            entry => ToRouteStageSource(entry, defaultCollageGenerationSettings));

        return source != null;
    }

    public bool TryRollStagePack(StageEntryKind kind, System.Random random, out BasePackData pack, out string displayName)
    {
        pack = null;
        displayName = null;
        var candidates = new List<Entry>();
        foreach (var item in entries ?? new List<Entry>())
        {
            if (enabled && item != null && item.enabled && item.kind == kind && item.stagePack != null)
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
            Debug.LogError($"[StagePoolSO] Failed to parse stage pack '{entry.stagePack.name}': {e.Message}");
            return false;
        }
    }

    protected override string GetEntryDisplayName(Entry entry, int index)
    {
        if (entry == null)
            return base.GetEntryDisplayName(entry, index);

        if (!string.IsNullOrWhiteSpace(entry.stageId))
            return entry.stageId;

        return entry.kind switch
        {
            StageEntryKind.ClassicLevel when entry.levelData != null => entry.levelData.name,
            StageEntryKind.Encounter when entry.stagePack != null => entry.stagePack.name,
            StageEntryKind.Shop when entry.shop != null => entry.shop.name,
            StageEntryKind.Escort => "Escort",
            _ => $"{entry.kind} {index}"
        };
    }

    private static RunRouteStageSource ToRouteStageSource(Entry entry, LevelCollageGenerationSettings defaultCollageGenerationSettings)
    {
        if (entry == null || !entry.enabled)
            return null;

        switch (entry.kind)
        {
            case StageEntryKind.ClassicLevel:
                if (entry.levelData == null)
                    return null;

                return new RunRouteStageSource
                {
                    stageId = string.IsNullOrWhiteSpace(entry.stageId) ? entry.levelData.name : entry.stageId,
                    stageType = entry.stageType,
                    weight = Mathf.Max(1, entry.weight),
                    levelData = entry.levelData,
                    contentKind = VFSContentKind.UnityObject,
                    contentSource = VFSContentSource.Reference,
                    unityObjectTypeName = typeof(LevelData).AssemblyQualifiedName,
                    assetPath = GetAssetPath(entry.levelData),
                    referencePath = GetResourcesPath(entry.levelData)
                };

            case StageEntryKind.Encounter:
                if (entry.stagePack == null)
                    return null;

                return new RunRouteStageSource
                {
                    stageId = string.IsNullOrWhiteSpace(entry.stageId) ? entry.stagePack.name : entry.stageId,
                    stageType = string.IsNullOrWhiteSpace(entry.stageType) ? "Encounter" : entry.stageType,
                    weight = Mathf.Max(1, entry.weight),
                    contentKind = VFSContentKind.Nekograph,
                    contentSource = VFSContentSource.Reference,
                    assetPath = GetAssetPath(entry.stagePack),
                    referencePath = GetResourcesPath(entry.stagePack)
                };

            case StageEntryKind.Shop:
                if (entry.shop == null)
                    return null;

                return new RunRouteStageSource
                {
                    stageId = string.IsNullOrWhiteSpace(entry.stageId) ? entry.shop.name : entry.stageId,
                    stageType = string.IsNullOrWhiteSpace(entry.stageType) ? "Shop" : entry.stageType,
                    weight = Mathf.Max(1, entry.weight),
                    shop = entry.shop,
                    contentKind = VFSContentKind.UnityObject,
                    contentSource = VFSContentSource.Reference,
                    unityObjectTypeName = typeof(ShopSO).AssemblyQualifiedName,
                    assetPath = GetAssetPath(entry.shop),
                    referencePath = GetResourcesPath(entry.shop)
                };

            case StageEntryKind.Escort:
                return new RunRouteStageSource
                {
                    stageId = string.IsNullOrWhiteSpace(entry.stageId) ? "Escort" : entry.stageId,
                    stageType = string.IsNullOrWhiteSpace(entry.stageType) ? "Escort" : entry.stageType,
                    weight = Mathf.Max(1, entry.weight),
                    collageGenerationSettings = entry.collageGenerationSettings != null
                        ? entry.collageGenerationSettings
                        : defaultCollageGenerationSettings,
                    contentKind = VFSContentKind.Json,
                    contentSource = VFSContentSource.Inline
                };

            default:
                return null;
        }
    }

    private static string GetAssetPath(UnityEngine.Object asset)
    {
#if UNITY_EDITOR
        return asset != null ? UnityEditor.AssetDatabase.GetAssetPath(asset) : string.Empty;
#else
        return string.Empty;
#endif
    }

    private static string GetResourcesPath(UnityEngine.Object asset)
    {
#if UNITY_EDITOR
        string assetPath = GetAssetPath(asset);
        if (string.IsNullOrWhiteSpace(assetPath))
            return string.Empty;

        string normalized = assetPath.Replace('\\', '/');
        const string marker = "/Resources/";
        int index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return string.Empty;

        string relative = normalized[(index + marker.Length)..];
        string extension = System.IO.Path.GetExtension(relative);
        return string.IsNullOrEmpty(extension) ? relative : relative[..^extension.Length];
#else
        return string.Empty;
#endif
    }
}
