using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "RunRouteShapeConfig", menuName = "BlockingKing/Run/Route Shape Config")]
public sealed class RunRouteShapeConfigSO : ScriptableObject
{
    [Title("Route Size")]
    [MinValue(1)]
    public int layerCount = 8;

    [MinValue(1)]
    public int laneCount = 4;

    [Title("Default Layer Width")]
    public bool forceFirstLayerSingleNode = true;
    public bool forceLastLayerSingleNode = true;

    [MinValue(1)]
    public int middleLayerMinNodes = 2;

    [MinValue(1)]
    public int middleLayerMaxNodes = 4;

    [Title("Layer Rules")]
    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 120)]
    public List<RouteLayerShapeRule> layerRules = new List<RouteLayerShapeRule>();

    public RunRouteShape BuildShape(System.Random random)
    {
        int resolvedLayerCount = Mathf.Max(1, layerCount);
        int resolvedLaneCount = Mathf.Max(1, laneCount);
        var shape = new RunRouteShape(resolvedLayerCount, resolvedLaneCount);

        int defaultMin = Mathf.Clamp(middleLayerMinNodes, 1, resolvedLaneCount);
        int defaultMax = Mathf.Clamp(Mathf.Max(middleLayerMinNodes, middleLayerMaxNodes), 1, resolvedLaneCount);
        if (defaultMin > defaultMax)
            (defaultMin, defaultMax) = (defaultMax, defaultMin);

        for (int layer = 0; layer < resolvedLayerCount; layer++)
        {
            int minNodes = defaultMin;
            int maxNodes = defaultMax;
            bool hasExplicitNodeCount = false;
            bool forceStageKind = false;
            StagePoolSO.StageEntryKind stageKind = StagePoolSO.StageEntryKind.ClassicLevel;

            foreach (var rule in layerRules ?? new List<RouteLayerShapeRule>())
            {
                if (rule == null || !rule.enabled || !rule.Matches(layer, resolvedLayerCount))
                    continue;

                if (rule.overrideNodeCount)
                {
                    minNodes = Mathf.Clamp(rule.minNodes, 1, resolvedLaneCount);
                    maxNodes = Mathf.Clamp(Mathf.Max(rule.minNodes, rule.maxNodes), 1, resolvedLaneCount);
                    if (minNodes > maxNodes)
                        (minNodes, maxNodes) = (maxNodes, minNodes);
                    hasExplicitNodeCount = true;
                }

                if (rule.forceStageKind)
                {
                    forceStageKind = true;
                    stageKind = rule.stageKind;
                }
            }

            if (!hasExplicitNodeCount &&
                ((layer == 0 && forceFirstLayerSingleNode) ||
                 (layer == resolvedLayerCount - 1 && forceLastLayerSingleNode)))
            {
                minNodes = 1;
                maxNodes = 1;
            }

            int nodeCount = minNodes == maxNodes
                ? minNodes
                : (random != null ? random.Next(minNodes, maxNodes + 1) : UnityEngine.Random.Range(minNodes, maxNodes + 1));

            shape.SetNodeCount(layer, Mathf.Clamp(nodeCount, 1, resolvedLaneCount));
            if (forceStageKind)
                shape.SetForcedStageKind(layer, stageKind);
        }

        return shape;
    }
}

[Serializable]
public sealed class RouteLayerShapeRule
{
    public bool enabled = true;
    public string label;

    [LabelText("Layer Min")]
    public int minLayer;

    [LabelText("Layer Max (-1 = last)")]
    public int maxLayer = -1;

    public bool overrideNodeCount;

    [ShowIf(nameof(overrideNodeCount)), MinValue(1)]
    public int minNodes = 1;

    [ShowIf(nameof(overrideNodeCount)), MinValue(1)]
    public int maxNodes = 1;

    public bool forceStageKind;

    [ShowIf(nameof(forceStageKind))]
    public StagePoolSO.StageEntryKind stageKind = StagePoolSO.StageEntryKind.ClassicLevel;

    public bool Matches(int layer, int layerCount)
    {
        int min = Mathf.Max(0, minLayer);
        int max = maxLayer < 0 ? Mathf.Max(0, layerCount - 1) : Mathf.Max(0, maxLayer);
        if (min > max)
            (min, max) = (max, min);

        return layer >= min && layer <= max;
    }
}

public sealed class RunRouteShape
{
    private readonly int[] _nodeCounts;
    private readonly bool[] _hasForcedStageKind;
    private readonly StagePoolSO.StageEntryKind[] _forcedStageKinds;

    public int LayerCount { get; }
    public int LaneCount { get; }

    public RunRouteShape(int layerCount, int laneCount)
    {
        LayerCount = Mathf.Max(1, layerCount);
        LaneCount = Mathf.Max(1, laneCount);
        _nodeCounts = new int[LayerCount];
        _hasForcedStageKind = new bool[LayerCount];
        _forcedStageKinds = new StagePoolSO.StageEntryKind[LayerCount];

        for (int i = 0; i < _nodeCounts.Length; i++)
            _nodeCounts[i] = 1;
    }

    public int GetNodeCount(int layer)
    {
        if (layer < 0 || layer >= _nodeCounts.Length)
            return 1;

        return Mathf.Clamp(_nodeCounts[layer], 1, LaneCount);
    }

    public void SetNodeCount(int layer, int nodeCount)
    {
        if (layer < 0 || layer >= _nodeCounts.Length)
            return;

        _nodeCounts[layer] = Mathf.Clamp(nodeCount, 1, LaneCount);
    }

    public bool TryGetForcedStageKind(int layer, out StagePoolSO.StageEntryKind kind)
    {
        kind = StagePoolSO.StageEntryKind.ClassicLevel;
        if (layer < 0 || layer >= _hasForcedStageKind.Length || !_hasForcedStageKind[layer])
            return false;

        kind = _forcedStageKinds[layer];
        return true;
    }

    public void SetForcedStageKind(int layer, StagePoolSO.StageEntryKind kind)
    {
        if (layer < 0 || layer >= _hasForcedStageKind.Length)
            return;

        _hasForcedStageKind[layer] = true;
        _forcedStageKinds[layer] = kind;
    }
}
