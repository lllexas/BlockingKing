using System;
using System.Collections.Generic;
using NekoGraph;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "GameFlowSettings", menuName = "BlockingKing/Game Flow Settings")]
public class GameFlowSettings : ScriptableObject
{
    [Serializable]
    public sealed class ClassicLevelStageSource
    {
        [TableColumnWidth(140)]
        public string stageId;

        [TableColumnWidth(76)]
        public string stageType;

        [MinValue(1)]
        [TableColumnWidth(48)]
        public int weight = 1;

        [AssetsOnly]
        public LevelData levelData;
    }

    [Serializable]
    public sealed class EncounterStageSource
    {
        [TableColumnWidth(140)]
        public string stageId;

        [TableColumnWidth(76)]
        public string stageType = "Encounter";

        [MinValue(1)]
        [TableColumnWidth(48)]
        public int weight = 1;

        [AssetsOnly]
        public TextAsset stagePack;
    }

    [Serializable]
    public sealed class ShopStageSource
    {
        [TableColumnWidth(140)]
        public string stageId;

        [TableColumnWidth(76)]
        public string stageType = "Shop";

        [MinValue(1)]
        [TableColumnWidth(48)]
        public int weight = 1;

        [AssetsOnly]
        public ShopSO shop;
    }

    [Serializable]
    public sealed class EscortStageSource
    {
        [TableColumnWidth(140)]
        public string stageId = "Escort";

        [TableColumnWidth(76)]
        public string stageType = "Escort";

        [MinValue(1)]
        [TableColumnWidth(48)]
        public int weight = 1;

        [AssetsOnly]
        public LevelCollageGenerationSettings collageGenerationSettings;
    }

    [Title("Route Layout")]
    [HorizontalGroup("Route", Width = 120)]
    [MinValue(1)]
    public int layerCount = 8;

    [HorizontalGroup("Route", Width = 120)]
    [MinValue(1)]
    public int laneCount = 4;

    [HorizontalGroup("Route", Width = 120)]
    public int seed;

    [Title("Difficulty")]
    [MinValue(0)]
    public float overallDifficulty = 1f;

    [AssetsOnly]
    public EnemySpawnDifficultyProfileSO enemySpawnDifficultyProfile;

    [ShowInInspector, ReadOnly, LabelText("Stage Sources")]
    [HorizontalGroup("Route")]
    private int StageSourceCount => BuildRouteStageSources().Count;

    [Title("Stage Pools")]
    [FoldoutGroup("Classic Levels", Expanded = true)]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<ClassicLevelStageSource> classicLevels = new List<ClassicLevelStageSource>();

    [FoldoutGroup("Encounters", Expanded = true)]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<EncounterStageSource> encounters = new List<EncounterStageSource>();

    [FoldoutGroup("Shops", Expanded = true)]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 80)]
    public List<ShopStageSource> shops = new List<ShopStageSource>();

    [FoldoutGroup("Escorts", Expanded = true)]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 80)]
    public List<EscortStageSource> escorts = new List<EscortStageSource>();

    [Title("Collage Defaults")]
    [AssetsOnly]
    public LevelCollageGenerationSettings defaultCollageGenerationSettings;

    [Title("Diagnostics")]
    [ShowInInspector, ReadOnly, LabelText("Weight Summary")]
    private string WeightSummary => BuildWeightSummary();

    [ShowInInspector, ReadOnly, LabelText("Escort Collage Status")]
    private string EscortCollageStatus => BuildEscortCollageStatus();

    [Button(ButtonSizes.Medium), HorizontalGroup("Actions")]
    private void NormalizeWeights()
    {
        foreach (var source in classicLevels ?? new List<ClassicLevelStageSource>())
            if (source != null) source.weight = Mathf.Max(1, source.weight);

        foreach (var source in encounters ?? new List<EncounterStageSource>())
            if (source != null) source.weight = Mathf.Max(1, source.weight);

        foreach (var source in shops ?? new List<ShopStageSource>())
            if (source != null) source.weight = Mathf.Max(1, source.weight);

        foreach (var source in escorts ?? new List<EscortStageSource>())
            if (source != null) source.weight = Mathf.Max(1, source.weight);
    }

    public List<RunRouteStageSource> BuildRouteStageSources()
    {
        var result = new List<RunRouteStageSource>();
        foreach (var source in classicLevels ?? new List<ClassicLevelStageSource>())
        {
            if (source?.levelData == null)
                continue;

            result.Add(new RunRouteStageSource
            {
                stageId = string.IsNullOrWhiteSpace(source.stageId) ? source.levelData.name : source.stageId,
                stageType = source.stageType,
                weight = Mathf.Max(1, source.weight),
                levelData = source.levelData,
                contentKind = VFSContentKind.UnityObject,
                contentSource = VFSContentSource.Reference,
                unityObjectTypeName = typeof(LevelData).AssemblyQualifiedName,
                assetPath = GetAssetPath(source.levelData),
                referencePath = GetResourcesPath(source.levelData)
            });
        }

        foreach (var source in encounters ?? new List<EncounterStageSource>())
        {
            if (source?.stagePack == null)
                continue;

            result.Add(new RunRouteStageSource
            {
                stageId = string.IsNullOrWhiteSpace(source.stageId) ? source.stagePack.name : source.stageId,
                stageType = source.stageType,
                weight = Mathf.Max(1, source.weight),
                contentKind = VFSContentKind.Nekograph,
                contentSource = VFSContentSource.Reference,
                assetPath = GetAssetPath(source.stagePack),
                referencePath = GetResourcesPath(source.stagePack)
            });
        }

        foreach (var source in shops ?? new List<ShopStageSource>())
        {
            if (source?.shop == null)
                continue;

            result.Add(new RunRouteStageSource
            {
                stageId = string.IsNullOrWhiteSpace(source.stageId) ? source.shop.name : source.stageId,
                stageType = string.IsNullOrWhiteSpace(source.stageType) ? "Shop" : source.stageType,
                weight = Mathf.Max(1, source.weight),
                shop = source.shop,
                contentKind = VFSContentKind.UnityObject,
                contentSource = VFSContentSource.Reference,
                unityObjectTypeName = typeof(ShopSO).AssemblyQualifiedName,
                assetPath = GetAssetPath(source.shop),
                referencePath = GetResourcesPath(source.shop)
            });
        }

        foreach (var source in escorts ?? new List<EscortStageSource>())
        {
            if (source == null)
                continue;

            result.Add(new RunRouteStageSource
            {
                stageId = string.IsNullOrWhiteSpace(source.stageId) ? "Escort" : source.stageId,
                stageType = string.IsNullOrWhiteSpace(source.stageType) ? "Escort" : source.stageType,
                weight = Mathf.Max(1, source.weight),
                collageGenerationSettings = source.collageGenerationSettings != null
                    ? source.collageGenerationSettings
                    : defaultCollageGenerationSettings,
                contentKind = VFSContentKind.Json,
                contentSource = VFSContentSource.Inline
            });
        }

        return result;
    }

    private string BuildWeightSummary()
    {
        int classicWeight = SumClassicWeight();
        int encounterWeight = SumEncounterWeight();
        int shopWeight = SumShopWeight();
        int escortWeight = SumEscortWeight();
        int total = classicWeight + encounterWeight + shopWeight + escortWeight;
        if (total <= 0)
            return "No active stage sources.";

        return $"Classic {classicWeight} ({classicWeight / (float)total:P0}) | " +
               $"Encounter {encounterWeight} ({encounterWeight / (float)total:P0}) | " +
               $"Shop {shopWeight} ({shopWeight / (float)total:P0}) | " +
               $"Escort {escortWeight} ({escortWeight / (float)total:P0}) | " +
               $"Total {total}";
    }

    private string BuildEscortCollageStatus()
    {
        if (escorts == null || escorts.Count == 0)
            return "No Escort stages configured.";

        int missingSettings = 0;
        int missingSource = 0;
        foreach (var source in escorts)
        {
            if (source == null)
                continue;

            var settings = source.collageGenerationSettings != null
                ? source.collageGenerationSettings
                : defaultCollageGenerationSettings;

            if (settings == null)
            {
                missingSettings++;
                continue;
            }

            if (settings.sourceDatabase == null)
                missingSource++;
        }

        if (missingSettings > 0)
            return $"{missingSettings} Escort source(s) missing collage generation settings.";

        if (missingSource > 0)
            return $"{missingSource} Escort source(s) missing source database on generation settings.";

        return "Escort collage settings ready.";
    }

    private int SumClassicWeight()
    {
        int total = 0;
        foreach (var source in classicLevels ?? new List<ClassicLevelStageSource>())
        {
            if (source?.levelData != null)
                total += Mathf.Max(1, source.weight);
        }

        return total;
    }

    private int SumEncounterWeight()
    {
        int total = 0;
        foreach (var source in encounters ?? new List<EncounterStageSource>())
        {
            if (source?.stagePack != null)
                total += Mathf.Max(1, source.weight);
        }

        return total;
    }

    private int SumShopWeight()
    {
        int total = 0;
        foreach (var source in shops ?? new List<ShopStageSource>())
        {
            if (source?.shop != null)
                total += Mathf.Max(1, source.weight);
        }

        return total;
    }

    private int SumEscortWeight()
    {
        int total = 0;
        foreach (var source in escorts ?? new List<EscortStageSource>())
        {
            if (source != null)
                total += Mathf.Max(1, source.weight);
        }

        return total;
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
