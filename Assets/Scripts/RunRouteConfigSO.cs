using System;
using System.Collections.Generic;
using NekoGraph;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "RunRouteConfig", menuName = "BlockingKing/Run/Route Config")]
public class RunRouteConfigSO : ScriptableObject
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

    [AssetsOnly]
    public RunRouteShapeConfigSO shapeConfig;

    [Title("Difficulty")]
    [Min(0)]
    public float overallDifficulty = 1f;

    [AssetsOnly]
    public EnemySpawnDifficultyProfileSO enemySpawnDifficultyProfile;

    [Title("Stage Type")]
    [AssetsOnly]
    public StageTypePoolSO stageTypePool;

    [Title("Stage Content Pools")]
    [AssetsOnly]
    public StagePoolSO stagePool;

    [AssetsOnly]
    public StagePoolSO classicStagePool;

    [AssetsOnly]
    public LevelFeatureSelectionTableSO classicLevelSelectionTable;

    [AssetsOnly]
    public StagePoolSO encounterStagePool;

    [AssetsOnly]
    public StagePoolSO shopStagePool;

    [AssetsOnly]
    public StagePoolSO escortStagePool;

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
        if (stagePool != null)
        {
            var pooledSources = stagePool.BuildRouteStageSources(defaultCollageGenerationSettings);
            if (pooledSources.Count > 0)
                return pooledSources;
        }

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

    public bool TryPickRouteStageSource(
        int routeLayer,
        int routeLayerCount,
        float difficulty,
        System.Random random,
        out RunRouteStageSource source)
    {
        return TryPickRouteStageSource(routeLayer, routeLayerCount, difficulty, null, random, out source);
    }

    public bool TryPickRouteStageSource(
        int routeLayer,
        int routeLayerCount,
        float difficulty,
        StagePoolSO.StageEntryKind? forcedKind,
        System.Random random,
        out RunRouteStageSource source)
    {
        source = null;
        var context = new PoolEvalContext
        {
            routeLayer = Mathf.Max(0, routeLayer),
            routeLayerCount = Mathf.Max(1, routeLayerCount),
            progress = routeLayerCount > 1 ? Mathf.Clamp01(routeLayer / (float)(routeLayerCount - 1)) : 0f,
            difficulty = Mathf.Max(0f, difficulty)
        };

        if (forcedKind.HasValue)
            return TryPickRouteStageSourceByKind(forcedKind.Value, context, random, out source);

        if (stageTypePool != null && stageTypePool.TryRoll(context, random, out var kind))
        {
            if (TryPickRouteStageSourceByKind(kind, context, random, out source))
                return true;
        }

        var sources = BuildRouteStageSources();
        source = PickWeightedStageSource(sources, random);
        return source != null;
    }

    public int GetResolvedLayerCount()
    {
        return shapeConfig != null ? Mathf.Max(1, shapeConfig.layerCount) : Mathf.Max(1, layerCount);
    }

    public int GetResolvedLaneCount()
    {
        return shapeConfig != null ? Mathf.Max(1, shapeConfig.laneCount) : Mathf.Max(1, laneCount);
    }

    public RunRouteShape BuildShape(System.Random random)
    {
        if (shapeConfig != null)
            return shapeConfig.BuildShape(random);

        int resolvedLayerCount = Mathf.Max(1, layerCount);
        int resolvedLaneCount = Mathf.Max(1, laneCount);
        var shape = new RunRouteShape(resolvedLayerCount, resolvedLaneCount);
        for (int layer = 0; layer < resolvedLayerCount; layer++)
        {
            int nodesInLayer = layer == 0 || layer == resolvedLayerCount - 1
                ? 1
                : Mathf.Clamp(random != null ? random.Next(2, resolvedLaneCount + 1) : UnityEngine.Random.Range(2, resolvedLaneCount + 1), 1, resolvedLaneCount);
            shape.SetNodeCount(layer, nodesInLayer);
        }

        return shape;
    }

    private bool TryPickRouteStageSourceByKind(
        StagePoolSO.StageEntryKind kind,
        PoolEvalContext context,
        System.Random random,
        out RunRouteStageSource source)
    {
        source = null;
        if (kind == StagePoolSO.StageEntryKind.ClassicLevel &&
            TryPickClassicLevelStageSource(context, random, out source))
        {
            return true;
        }

        StagePoolSO pool = kind switch
        {
            StagePoolSO.StageEntryKind.ClassicLevel => classicStagePool,
            StagePoolSO.StageEntryKind.Encounter => encounterStagePool,
            StagePoolSO.StageEntryKind.Shop => shopStagePool,
            StagePoolSO.StageEntryKind.Escort => escortStagePool,
            _ => null
        };

        if (pool != null && pool.TryRollSource(kind, defaultCollageGenerationSettings, random, out source))
            return true;

        if (stagePool != null && stagePool.TryRollSource(kind, defaultCollageGenerationSettings, random, out source))
            return true;

        var filteredSources = BuildLegacyRouteStageSources(kind);
        source = PickWeightedStageSource(filteredSources, random);
        return source != null;
    }

    private bool TryPickClassicLevelStageSource(PoolEvalContext context, System.Random random, out RunRouteStageSource source)
    {
        source = null;

        if (classicLevelSelectionTable != null && classicLevelSelectionTable.TryRollLevel(context, random, out var level))
        {
            source = CreateClassicStageSource(level);
            return source != null;
        }

        var pool = classicStagePool;
        if (pool != null && pool.TryRollSource(StagePoolSO.StageEntryKind.ClassicLevel, defaultCollageGenerationSettings, random, out source))
            return true;

        if (classicLevels != null && classicLevels.Count > 0)
        {
            var legacy = new List<RunRouteStageSource>();
            foreach (var sourceItem in classicLevels)
            {
                if (sourceItem?.levelData == null)
                    continue;

                legacy.Add(new RunRouteStageSource
                {
                    stageId = string.IsNullOrWhiteSpace(sourceItem.stageId) ? sourceItem.levelData.name : sourceItem.stageId,
                    stageType = sourceItem.stageType,
                    weight = Mathf.Max(1, sourceItem.weight),
                    levelData = sourceItem.levelData,
                    contentKind = VFSContentKind.UnityObject,
                    contentSource = VFSContentSource.Reference,
                    unityObjectTypeName = typeof(LevelData).AssemblyQualifiedName,
                    assetPath = GetAssetPath(sourceItem.levelData),
                    referencePath = GetResourcesPath(sourceItem.levelData)
                });
            }

            source = PickWeightedStageSource(legacy, random);
            return source != null;
        }

        return false;
    }

    private static RunRouteStageSource CreateClassicStageSource(LevelData level)
    {
        if (level == null)
            return null;

        return new RunRouteStageSource
        {
            stageId = level.name,
            stageType = "Classic",
            weight = 1,
            levelData = level,
            contentKind = VFSContentKind.UnityObject,
            contentSource = VFSContentSource.Reference,
            unityObjectTypeName = typeof(LevelData).AssemblyQualifiedName,
            assetPath = GetAssetPath(level),
            referencePath = GetResourcesPath(level)
        };
    }

    private List<RunRouteStageSource> BuildLegacyRouteStageSources(StagePoolSO.StageEntryKind kind)
    {
        var allSources = BuildRouteStageSources();
        var result = new List<RunRouteStageSource>();
        foreach (var source in allSources)
        {
            if (source == null)
                continue;

            if (MatchesKind(source, kind))
                result.Add(source);
        }

        return result;
    }

    private static bool MatchesKind(RunRouteStageSource source, StagePoolSO.StageEntryKind kind)
    {
        return kind switch
        {
            StagePoolSO.StageEntryKind.ClassicLevel => source.levelData != null,
            StagePoolSO.StageEntryKind.Encounter => source.contentKind == VFSContentKind.Nekograph,
            StagePoolSO.StageEntryKind.Shop => source.shop != null,
            StagePoolSO.StageEntryKind.Escort => source.contentKind == VFSContentKind.Json,
            _ => false
        };
    }

    private static RunRouteStageSource PickWeightedStageSource(IReadOnlyList<RunRouteStageSource> sources, System.Random random)
    {
        if (sources == null || sources.Count == 0)
            return null;

        int totalWeight = 0;
        for (int i = 0; i < sources.Count; i++)
            totalWeight += Mathf.Max(0, sources[i]?.weight ?? 0);

        if (totalWeight <= 0)
            return sources[random.Next(sources.Count)];

        int pick = random.Next(totalWeight);
        for (int i = 0; i < sources.Count; i++)
        {
            var candidate = sources[i];
            int weight = Mathf.Max(0, candidate?.weight ?? 0);
            if (pick < weight)
                return candidate;

            pick -= weight;
        }

        return sources[^1];
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
