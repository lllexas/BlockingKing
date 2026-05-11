using System;
using System.Collections.Generic;
using NekoGraph;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "RunRouteConfig", menuName = "BlockingKing/Run/Route Config")]
public class RunRouteConfigSO : ScriptableObject
{
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

    [Title("Collage Defaults")]
    [AssetsOnly]
    public LevelCollageGenerationSettings defaultCollageGenerationSettings;

    [Title("Diagnostics")]
    [ShowInInspector, ReadOnly, LabelText("Stage Sources")]
    [HorizontalGroup("Route")]
    private int StageSourceCount => BuildRouteStageSources().Count;

    [ShowInInspector, ReadOnly, LabelText("Configured Pools")]
    private string ConfiguredPoolSummary => BuildConfiguredPoolSummary();

    public List<RunRouteStageSource> BuildRouteStageSources()
    {
        var result = new List<RunRouteStageSource>();
        AddPoolSources(result, stagePool, null);
        AddPoolSources(result, classicStagePool, StagePoolSO.StageEntryKind.ClassicLevel);
        AddPoolSources(result, encounterStagePool, StagePoolSO.StageEntryKind.Encounter);
        AddPoolSources(result, shopStagePool, StagePoolSO.StageEntryKind.Shop);
        AddPoolSources(result, escortStagePool, StagePoolSO.StageEntryKind.Escort);
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
        random ??= new System.Random();
        var context = new PoolEvalContext
        {
            routeLayer = Mathf.Max(0, routeLayer),
            routeLayerCount = Mathf.Max(1, routeLayerCount),
            progress = routeLayerCount > 1 ? Mathf.Clamp01(routeLayer / (float)(routeLayerCount - 1)) : 0f,
            difficulty = Mathf.Max(0f, difficulty)
        };

        if (forcedKind.HasValue)
            return TryPickRouteStageSourceByKind(forcedKind.Value, context, random, out source);

        if (stageTypePool != null && stageTypePool.TryRoll(context, random, out var kind) &&
            TryPickRouteStageSourceByKind(kind, context, random, out source))
        {
            return true;
        }

        source = PickWeightedStageSource(BuildRouteStageSources(), random);
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
        random ??= new System.Random();
        if (shapeConfig != null)
            return shapeConfig.BuildShape(random);

        int resolvedLayerCount = Mathf.Max(1, layerCount);
        int resolvedLaneCount = Mathf.Max(1, laneCount);
        var shape = new RunRouteShape(resolvedLayerCount, resolvedLaneCount);
        for (int layer = 0; layer < resolvedLayerCount; layer++)
        {
            int nodesInLayer = layer == 0 || layer == resolvedLayerCount - 1
                ? 1
                : Mathf.Clamp(random.Next(2, resolvedLaneCount + 1), 1, resolvedLaneCount);
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

        var pool = GetTypedPool(kind);
        if (pool != null && pool.TryRollSource(kind, defaultCollageGenerationSettings, random, out source))
            return true;

        if (stagePool != null && stagePool.TryRollSource(kind, defaultCollageGenerationSettings, random, out source))
            return true;

        source = PickWeightedStageSource(BuildRouteStageSources(kind), random);
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

        if (classicStagePool != null &&
            classicStagePool.TryRollSource(StagePoolSO.StageEntryKind.ClassicLevel, defaultCollageGenerationSettings, random, out source))
        {
            return true;
        }

        return stagePool != null &&
               stagePool.TryRollSource(StagePoolSO.StageEntryKind.ClassicLevel, defaultCollageGenerationSettings, random, out source);
    }

    private StagePoolSO GetTypedPool(StagePoolSO.StageEntryKind kind)
    {
        return kind switch
        {
            StagePoolSO.StageEntryKind.ClassicLevel => classicStagePool,
            StagePoolSO.StageEntryKind.Encounter => encounterStagePool,
            StagePoolSO.StageEntryKind.Shop => shopStagePool,
            StagePoolSO.StageEntryKind.Escort => escortStagePool,
            _ => null
        };
    }

    private List<RunRouteStageSource> BuildRouteStageSources(StagePoolSO.StageEntryKind kind)
    {
        var result = new List<RunRouteStageSource>();
        AddPoolSources(result, GetTypedPool(kind), kind);
        AddPoolSources(result, stagePool, kind);
        return result;
    }

    private void AddPoolSources(List<RunRouteStageSource> target, StagePoolSO pool, StagePoolSO.StageEntryKind? kindFilter)
    {
        if (target == null || pool == null)
            return;

        target.AddRange(pool.BuildRouteStageSources(defaultCollageGenerationSettings, kindFilter));
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

    private static RunRouteStageSource PickWeightedStageSource(IReadOnlyList<RunRouteStageSource> sources, System.Random random)
    {
        if (sources == null || sources.Count == 0)
            return null;

        random ??= new System.Random();
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

    private string BuildConfiguredPoolSummary()
    {
        var parts = new List<string>();
        AppendPoolSummary(parts, "Mixed", stagePool, null);
        AppendPoolSummary(parts, "Classic", classicStagePool, StagePoolSO.StageEntryKind.ClassicLevel);
        AppendPoolSummary(parts, "Encounter", encounterStagePool, StagePoolSO.StageEntryKind.Encounter);
        AppendPoolSummary(parts, "Shop", shopStagePool, StagePoolSO.StageEntryKind.Shop);
        AppendPoolSummary(parts, "Escort", escortStagePool, StagePoolSO.StageEntryKind.Escort);

        if (classicLevelSelectionTable != null)
            parts.Add($"Classic Table: {classicLevelSelectionTable.name}");

        return parts.Count > 0 ? string.Join(" | ", parts) : "No route stage pools configured.";
    }

    private void AppendPoolSummary(List<string> parts, string label, StagePoolSO pool, StagePoolSO.StageEntryKind? kindFilter)
    {
        if (pool == null)
            return;

        int count = pool.BuildRouteStageSources(defaultCollageGenerationSettings, kindFilter).Count;
        parts.Add($"{label}: {count}");
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
