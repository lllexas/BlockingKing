using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NekoGraph;
using UnityEngine;

public class RunRouteFacade : PackFacadeBase
{
    public const string DefaultPackID = "runroute";

    private const int ReadSubject = PackAccessSubjects.Player;
    private const int WriteSubject = PackAccessSubjects.SystemMin;

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    private readonly Dictionary<string, LevelData> _runtimeLevelObjects = new Dictionary<string, LevelData>();

    protected override string GetDefaultPackID()
    {
        return DefaultPackID;
    }

    public BasePackData EnsureRoutePack()
    {
        var analyser = GraphHub.Instance?.DefaultAnalyser;
        if (analyser == null)
        {
            Debug.LogError("[RunRouteFacade] GraphAnalyser is not available.");
            return null;
        }

        var pack = analyser.EnsurePack(ResolvedPackID, WriteSubject);
        if (pack == null)
            return null;

        analyser.EnsurePackRoot(pack);
        return pack;
    }

    public BasePackData GenerateRoute(IReadOnlyList<RunRouteStageSource> stageSources, int layerCount = 8, int laneCount = 4, int seed = 0)
    {
        if (stageSources == null || stageSources.Count == 0)
        {
            Debug.LogError("[RunRouteFacade] GenerateRoute failed: stageSources is empty.");
            return null;
        }

        layerCount = Mathf.Max(1, layerCount);
        laneCount = Mathf.Max(1, laneCount);

        var pack = EnsureRoutePack();
        if (pack == null)
            return null;

        ResetPack(pack);
        _runtimeLevelObjects.Clear();

        var random = seed == 0 ? new System.Random() : new System.Random(seed);
        var byLayer = new List<List<VFSNodeData>>();

        for (int layer = 0; layer < layerCount; layer++)
        {
            int nodesInLayer = layer == 0 || layer == layerCount - 1
                ? 1
                : Mathf.Clamp(random.Next(2, laneCount + 1), 1, laneCount);

            var lanes = PickDistinctLanes(random, laneCount, nodesInLayer);
            var layerNodes = new List<VFSNodeData>();
            for (int i = 0; i < lanes.Count; i++)
            {
                int lane = lanes[i];
                var source = stageSources[random.Next(stageSources.Count)];
                var node = CreateStageNode(source, layer, lane, layer == 0);
                pack.Nodes[node.NodeID] = node;
                if (source.levelData != null)
                    _runtimeLevelObjects[node.NodeID] = source.levelData;
                layerNodes.Add(node);
            }

            byLayer.Add(layerNodes);
        }

        ConnectRoot(pack, byLayer[0]);
        for (int layer = 0; layer < byLayer.Count - 1; layer++)
            ConnectLayers(byLayer[layer], byLayer[layer + 1]);

        return pack;
    }

    public RunRouteView GetRouteView()
    {
        var pack = EnsureRoutePack();
        var view = new RunRouteView();
        if (pack == null)
            return view;

        foreach (var node in pack.Nodes.Values)
        {
            if (node is not VFSNodeData vfs || !string.Equals(vfs.Extension, ".stage", StringComparison.OrdinalIgnoreCase))
                continue;

            var routeNode = new RunRouteNodeView
            {
                nodeId = vfs.NodeID,
                name = vfs.Name,
                side = ReadSide(vfs),
                outgoingNodeIds = new List<string>(vfs.ChildNodeIDs ?? new List<string>())
            };

            view.nodes.Add(routeNode);
            foreach (var target in routeNode.outgoingNodeIds)
            {
                view.edges.Add(new RunRouteEdgeView
                {
                    fromNodeId = vfs.NodeID,
                    toNodeId = target
                });
            }
        }

        return view;
    }

    public List<RunRouteNodeView> GetAvailableNodes()
    {
        var result = new List<RunRouteNodeView>();
        foreach (var node in GetRouteView().nodes)
        {
            if (node.side.state == RunRouteNodeState.Available)
                result.Add(node);
        }

        return result;
    }

    public bool MarkNodeCompleted(string nodeId)
    {
        var pack = EnsureRoutePack();
        if (pack == null || string.IsNullOrWhiteSpace(nodeId))
            return false;

        if (!pack.Nodes.TryGetValue(nodeId, out var rawNode) || rawNode is not VFSNodeData node)
            return false;

        var side = ReadSide(node);
        side.state = RunRouteNodeState.Completed;
        WriteSide(node, side);

        foreach (var childId in node.ChildNodeIDs ?? new List<string>())
        {
            if (!pack.Nodes.TryGetValue(childId, out var childRaw) || childRaw is not VFSNodeData child)
                continue;

            var childSide = ReadSide(child);
            if (childSide.state == RunRouteNodeState.Locked)
            {
                childSide.state = RunRouteNodeState.Available;
                WriteSide(child, childSide);
            }
        }

        return true;
    }

    public bool TryGetStageNode(string nodeId, out VFSNodeData node)
    {
        node = null;
        var pack = EnsureRoutePack();
        if (pack == null || string.IsNullOrWhiteSpace(nodeId))
            return false;

        if (!pack.Nodes.TryGetValue(nodeId, out var rawNode) || rawNode is not VFSNodeData vfs)
            return false;

        if (!string.Equals(vfs.Extension, ".stage", StringComparison.OrdinalIgnoreCase))
            return false;

        node = vfs;
        return true;
    }

    public bool TryStartRouteNode(string nodeId)
    {
        if (IsRouteNodeRunning)
            return false;

        if (!TryGetStageNode(nodeId, out var node))
            return false;

        var side = ReadSide(node);
        if (side.state != RunRouteNodeState.Available)
            return false;

        var content = VFSContentResolver.Resolve(node);
        var level = ResolveRouteLevel(nodeId, content);
        if (level != null)
        {
            var player = UnityEngine.Object.FindObjectOfType<LevelPlayer>();
            if (player == null)
            {
                Debug.LogWarning("[RunRouteFacade] LevelPlayer not found.");
                return false;
            }

            bool started = player.PlayLevel(new LevelPlayRequest
            {
                Level = level,
                Mode = ResolveLevelPlayMode(side.stageType),
                StepLimit = ResolveStepLimit(side.stageType, 30)
            });

            if (!started)
                return false;

            BeginRouteNode(nodeId, side, isClassicLevel: true);
            GameFlowController.Instance?.OnRouteClassicLevelStarted();
            return true;
        }

        if (content == null)
            return false;

        var stagePack = content.GetNekographPack();
        if (stagePack != null)
        {
            var stageFacade = GraphHub.Instance?.GetFacade<RunStageFacade>();
            if (stageFacade == null)
            {
                stageFacade = new RunStageFacade();
                GraphHub.Instance?.RegisterFacade(stageFacade);
            }

            bool started = stageFacade != null && stageFacade.TryRunStagePack(stagePack, unloadPrevious: true);
            if (started)
            {
                BeginRouteNode(nodeId, side, isClassicLevel: false);
                GameFlowController.Instance?.OnRouteEncounterStarted();
            }

            return started;
        }

        Debug.LogWarning($"[RunRouteFacade] Unsupported stage content for node '{nodeId}': {content.Kind}");
        return false;
    }

    public string ActiveRouteNodeId { get; private set; }
    public string ActiveRouteStageType { get; private set; }
    public bool ActiveRouteNodeIsClassicLevel { get; private set; }
    public bool IsRouteNodeRunning => !string.IsNullOrWhiteSpace(ActiveRouteNodeId);

    public bool CompleteActiveRouteNode()
    {
        if (!IsRouteNodeRunning)
            return false;

        string nodeId = ActiveRouteNodeId;
        ClearActiveRouteNode();
        return MarkNodeCompleted(nodeId);
    }

    public void ClearActiveRouteNode()
    {
        ActiveRouteNodeId = null;
        ActiveRouteStageType = null;
        ActiveRouteNodeIsClassicLevel = false;
    }

    private void BeginRouteNode(string nodeId, RunRouteNodeSideData side, bool isClassicLevel)
    {
        ActiveRouteNodeId = nodeId;
        ActiveRouteStageType = side?.stageType;
        ActiveRouteNodeIsClassicLevel = isClassicLevel;
    }

    private LevelData ResolveRouteLevel(string nodeId, VFSResolvedContent content)
    {
        if (!string.IsNullOrWhiteSpace(nodeId) &&
            _runtimeLevelObjects.TryGetValue(nodeId, out var runtimeLevel) &&
            runtimeLevel != null)
        {
            return runtimeLevel;
        }

        return content?.GetUnityObject<LevelData>();
    }

    private static LevelPlayMode ResolveLevelPlayMode(string stageType)
    {
        if (string.IsNullOrWhiteSpace(stageType))
            return LevelPlayMode.Classic;

        string normalized = stageType.Trim();
        int separator = normalized.IndexOf(':');
        if (separator >= 0)
            normalized = normalized[..separator];

        return string.Equals(normalized, "steplimit", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "step_limit", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "step-limit", StringComparison.OrdinalIgnoreCase)
            ? LevelPlayMode.StepLimit
            : LevelPlayMode.Classic;
    }

    private static int ResolveStepLimit(string stageType, int fallback)
    {
        if (string.IsNullOrWhiteSpace(stageType))
            return fallback;

        int separator = stageType.IndexOf(':');
        if (separator < 0 || separator >= stageType.Length - 1)
            return fallback;

        return int.TryParse(stageType[(separator + 1)..], out int value)
            ? Mathf.Max(1, value)
            : fallback;
    }

    private static void ResetPack(BasePackData pack)
    {
        pack.Nodes.Clear();

        var root = new RootNodeData
        {
            NodeID = "root_runroute",
            Name = "Root",
            EditorPosition = new SerializableVector2(0f, 0f),
            OutputConnections = new List<ConnectionData>(),
            _ = new List<string>()
        };

        pack.RootNodeId = root.NodeID;
        pack.Nodes[root.NodeID] = root;
    }

    private static VFSNodeData CreateStageNode(RunRouteStageSource source, int layer, int lane, bool available)
    {
        string nodeId = $"stage_{layer}_{lane}_{Guid.NewGuid():N}"[..24];
        var side = new RunRouteNodeSideData
        {
            layer = layer,
            lane = lane,
            stageId = source.stageId,
            stageType = source.stageType,
            state = available ? RunRouteNodeState.Available : RunRouteNodeState.Locked
        };

        var node = new VFSNodeData
        {
            NodeID = nodeId,
            Name = string.IsNullOrWhiteSpace(source.stageId) ? $"stage_{layer}_{lane}" : source.stageId,
            Extension = ".stage",
            ContentKind = source.contentKind,
            ContentSource = source.contentSource,
            InlineText = JsonConvert.SerializeObject(side, JsonSettings),
            ReferencePath = source.referencePath,
            AssetGuid = source.assetGuid,
            AssetPath = source.assetPath,
            UnityObjectTypeName = source.unityObjectTypeName,
            IsEnabled = true,
            EditorPosition = new SerializableVector2(layer * 260f, lane * 120f),
            ChildNodeIDs = new List<string>(),
            OutputConnections = new List<ConnectionData>()
        };

        if (source.contentKind == VFSContentKind.Nekograph && source.contentSource == VFSContentSource.Inline)
        {
            Debug.LogWarning("[RunRouteFacade] .stage route nodes reserve InlineText for side data. Nekograph stages should use Reference source.");
            node.ContentSource = VFSContentSource.Reference;
        }

        return node;
    }

    private static void ConnectRoot(BasePackData pack, List<VFSNodeData> firstLayer)
    {
        if (!pack.Nodes.TryGetValue(pack.RootNodeId, out var rootRaw) || rootRaw is not RootNodeData root)
            return;

        foreach (var node in firstLayer)
        {
            root._.Add(node.NodeID);
            root.OutputConnections.Add(new ConnectionData(root.NodeID, 0, node.NodeID, 0));
        }
    }

    private static void ConnectLayers(List<VFSNodeData> current, List<VFSNodeData> next)
    {
        for (int i = 0; i < current.Count; i++)
        {
            var from = current[i];
            var nearest = FindNearestByLane(from, next);
            AddEdge(from, nearest);

            if (next.Count > 1 && i % 2 == 0)
            {
                var alternate = next[Mathf.Min(next.Count - 1, next.IndexOf(nearest) + 1)];
                AddEdge(from, alternate);
            }
        }

        foreach (var to in next)
        {
            bool hasIncoming = false;
            foreach (var from in current)
            {
                if (from.ChildNodeIDs.Contains(to.NodeID))
                {
                    hasIncoming = true;
                    break;
                }
            }

            if (!hasIncoming)
                AddEdge(FindNearestByLane(to, current), to);
        }
    }

    private static VFSNodeData FindNearestByLane(VFSNodeData origin, List<VFSNodeData> candidates)
    {
        var side = ReadSide(origin);
        VFSNodeData best = candidates[0];
        int bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            int distance = Mathf.Abs(ReadSide(candidate).lane - side.lane);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static void AddEdge(VFSNodeData from, VFSNodeData to)
    {
        if (from == null || to == null || from.ChildNodeIDs.Contains(to.NodeID))
            return;

        from.ChildNodeIDs.Add(to.NodeID);
        from.OutputConnections.Add(new ConnectionData(from.NodeID, 0, to.NodeID, 0));
        to.ParentNodeID = from.NodeID;
    }

    private static List<int> PickDistinctLanes(System.Random random, int laneCount, int count)
    {
        var lanes = new List<int>();
        for (int lane = 0; lane < laneCount; lane++)
            lanes.Add(lane);

        for (int i = 0; i < lanes.Count; i++)
        {
            int swapIndex = random.Next(i, lanes.Count);
            (lanes[i], lanes[swapIndex]) = (lanes[swapIndex], lanes[i]);
        }

        lanes.RemoveRange(count, lanes.Count - count);
        lanes.Sort();
        return lanes;
    }

    private static RunRouteNodeSideData ReadSide(VFSNodeData node)
    {
        try
        {
            return JsonConvert.DeserializeObject<RunRouteNodeSideData>(node.InlineText ?? string.Empty) ?? new RunRouteNodeSideData();
        }
        catch
        {
            return new RunRouteNodeSideData();
        }
    }

    private static void WriteSide(VFSNodeData node, RunRouteNodeSideData side)
    {
        node.InlineText = JsonConvert.SerializeObject(side ?? new RunRouteNodeSideData(), JsonSettings);
    }
}

[Serializable]
public sealed class RunRouteStageSource
{
    public string stageId;
    public string stageType;
    public int weight = 1;
    [JsonIgnore]
    public LevelData levelData;
    public VFSContentKind contentKind = VFSContentKind.UnityObject;
    public VFSContentSource contentSource = VFSContentSource.Reference;
    public string referencePath;
    public string assetGuid;
    public string assetPath;
    public string unityObjectTypeName;
}

[Serializable]
public sealed class RunRouteNodeSideData
{
    public int layer;
    public int lane;
    public string stageId;
    public string stageType;
    public RunRouteNodeState state;
}

public enum RunRouteNodeState
{
    Locked,
    Available,
    Completed
}

public sealed class RunRouteView
{
    public readonly List<RunRouteNodeView> nodes = new List<RunRouteNodeView>();
    public readonly List<RunRouteEdgeView> edges = new List<RunRouteEdgeView>();
}

public sealed class RunRouteNodeView
{
    public string nodeId;
    public string name;
    public RunRouteNodeSideData side;
    public List<string> outgoingNodeIds;
}

public sealed class RunRouteEdgeView
{
    public string fromNodeId;
    public string toNodeId;
}
