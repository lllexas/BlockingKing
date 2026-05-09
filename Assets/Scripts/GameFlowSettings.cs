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

        [TableColumnWidth(48)]
        public int weight = 1;

        [AssetsOnly]
        public TextAsset stagePack;
    }

    [Serializable]
    public sealed class EscortStageSource
    {
        [TableColumnWidth(140)]
        public string stageId = "Escort";

        [TableColumnWidth(76)]
        public string stageType = "Escort";

        [TableColumnWidth(48)]
        public int weight = 1;
    }

    [Header("Route Generation")]
    public int layerCount = 8;
    public int laneCount = 4;
    public int seed;

    [Title("经典关卡")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<ClassicLevelStageSource> classicLevels = new List<ClassicLevelStageSource>();

    [Title("不期而遇")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<EncounterStageSource> encounters = new List<EncounterStageSource>();

    [Title("带球/押送")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 80)]
    public List<EscortStageSource> escorts = new List<EscortStageSource>();

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

        foreach (var source in escorts ?? new List<EscortStageSource>())
        {
            if (source == null)
                continue;

            result.Add(new RunRouteStageSource
            {
                stageId = string.IsNullOrWhiteSpace(source.stageId) ? "Escort" : source.stageId,
                stageType = string.IsNullOrWhiteSpace(source.stageType) ? "Escort" : source.stageType,
                weight = Mathf.Max(1, source.weight),
                contentKind = VFSContentKind.Json,
                contentSource = VFSContentSource.Inline
            });
        }

        return result;
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
