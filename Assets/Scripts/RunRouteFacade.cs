using System;
using System.Collections.Generic;
using System.Globalization;
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
    private readonly Dictionary<string, LevelCollageGenerationSettings> _runtimeCollageSettings = new Dictionary<string, LevelCollageGenerationSettings>();
    private readonly Dictionary<string, ShopSO> _runtimeShopObjects = new Dictionary<string, ShopSO>();

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

    public BasePackData GenerateRoute(
        IReadOnlyList<RunRouteStageSource> stageSources,
        int layerCount = 8,
        int laneCount = 4,
        int seed = 0)
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
        _runtimeCollageSettings.Clear();
        _runtimeShopObjects.Clear();

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
                var source = PickWeightedStageSource(stageSources, random);
                if (source == null)
                    continue;

                var node = CreateStageNode(source, layer, lane, layer == 0);
                pack.Nodes[node.NodeID] = node;
                if (source.levelData != null)
                    _runtimeLevelObjects[node.NodeID] = source.levelData;
                if (source.collageGenerationSettings != null)
                    _runtimeCollageSettings[node.NodeID] = source.collageGenerationSettings;
                if (source.shop != null)
                    _runtimeShopObjects[node.NodeID] = source.shop;
                layerNodes.Add(node);
            }

            if (layerNodes.Count == 0)
            {
                Debug.LogError($"[RunRouteFacade] GenerateRoute failed: no valid stage source for layer {layer}.");
                return null;
            }

            byLayer.Add(layerNodes);
        }

        ConnectRoot(pack, byLayer[0]);
        for (int layer = 0; layer < byLayer.Count - 1; layer++)
            ConnectLayers(byLayer[layer], byLayer[layer + 1]);

        GenerateEscortLevelsForRoute(pack);
        return pack;
    }

    public BasePackData GenerateRoute(RunRouteConfigSO routeConfig)
    {
        if (routeConfig == null)
        {
            Debug.LogError("[RunRouteFacade] GenerateRoute failed: routeConfig is null.");
            return null;
        }

        var pack = EnsureRoutePack();
        if (pack == null)
            return null;

        ResetPack(pack);
        _runtimeLevelObjects.Clear();
        _runtimeCollageSettings.Clear();
        _runtimeShopObjects.Clear();

        var random = routeConfig.seed == 0 ? new System.Random() : new System.Random(routeConfig.seed);
        var shape = routeConfig.BuildShape(random);
        int layerCount = shape.LayerCount;
        int laneCount = shape.LaneCount;
        var byLayer = new List<List<VFSNodeData>>();

        for (int layer = 0; layer < layerCount; layer++)
        {
            int nodesInLayer = shape.GetNodeCount(layer);
            var lanes = PickDistinctLanes(random, laneCount, nodesInLayer);
            var layerNodes = new List<VFSNodeData>();
            for (int i = 0; i < lanes.Count; i++)
            {
                int lane = lanes[i];
                StagePoolSO.StageEntryKind? forcedKind = shape.TryGetForcedStageKind(layer, out var kind) ? kind : null;
                if (!routeConfig.TryPickRouteStageSource(layer, layerCount, routeConfig.overallDifficulty, forcedKind, random, out var source) ||
                    source == null)
                {
                    continue;
                }

                var node = CreateStageNode(source, layer, lane, layer == 0);
                pack.Nodes[node.NodeID] = node;
                RegisterRuntimeSource(node.NodeID, source);
                layerNodes.Add(node);
            }

            if (layerNodes.Count == 0)
            {
                Debug.LogError($"[RunRouteFacade] GenerateRoute failed: no valid stage source for layer {layer}.");
                return null;
            }

            byLayer.Add(layerNodes);
        }

        ConnectRoot(pack, byLayer[0]);
        for (int layer = 0; layer < byLayer.Count - 1; layer++)
            ConnectLayers(byLayer[layer], byLayer[layer + 1]);

        GenerateEscortLevelsForRoute(pack);
        return pack;
    }

    private void RegisterRuntimeSource(string nodeId, RunRouteStageSource source)
    {
        if (source == null || string.IsNullOrWhiteSpace(nodeId))
            return;

        if (source.levelData != null)
            _runtimeLevelObjects[nodeId] = source.levelData;
        if (source.collageGenerationSettings != null)
            _runtimeCollageSettings[nodeId] = source.collageGenerationSettings;
        if (source.shop != null)
            _runtimeShopObjects[nodeId] = source.shop;
    }

    private static RunRouteStageSource PickWeightedStageSource(IReadOnlyList<RunRouteStageSource> stageSources, System.Random random)
    {
        if (stageSources == null || stageSources.Count == 0)
            return null;

        int totalWeight = 0;
        for (int i = 0; i < stageSources.Count; i++)
            totalWeight += Mathf.Max(0, stageSources[i]?.weight ?? 0);

        if (totalWeight <= 0)
            return stageSources[random.Next(stageSources.Count)];

        int pick = random.Next(totalWeight);
        for (int i = 0; i < stageSources.Count; i++)
        {
            var source = stageSources[i];
            int weight = Mathf.Max(0, source?.weight ?? 0);
            if (pick < weight)
                return source;

            pick -= weight;
        }

        return stageSources[stageSources.Count - 1];
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

            var flow = GameFlowController.Instance;
            int routeLayerCount = flow != null ? flow.RouteLayerCount : 1;
            bool started = player.PlayLevel(new LevelPlayRequest
            {
                Level = level,
                Mode = ResolveLevelPlayMode(side.stageType),
                StepLimit = ResolveStepLimit(side.stageType, 30),
                Difficulty = flow != null
                    ? flow.BuildDifficultySnapshot(side.layer, routeLayerCount)
                    : RunDifficultySnapshot.Default,
                RewardSettings = flow != null ? flow.RewardSettings : null,
                RouteLayer = side.layer,
                RouteLayerCount = routeLayerCount
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
        if (stagePack == null)
            stagePack = ResolveShopStagePack(nodeId, content, side);

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

    public bool FailActiveRouteNode()
    {
        if (!IsRouteNodeRunning)
            return false;

        ClearActiveRouteNode();
        return true;
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

    private BasePackData ResolveShopStagePack(string nodeId, VFSResolvedContent content, RunRouteNodeSideData side)
    {
        ShopSO shop = null;
        if (!string.IsNullOrWhiteSpace(nodeId))
            _runtimeShopObjects.TryGetValue(nodeId, out shop);

        shop ??= content?.GetUnityObject<ShopSO>();
        return shop != null ? CreateShopStagePack(nodeId, shop, side) : null;
    }

    private static BasePackData CreateShopStagePack(string routeNodeId, ShopSO shop, RunRouteNodeSideData side)
    {
        string suffix = StableSeed($"{routeNodeId}:{shop?.name}").ToString("x8");
        string rootId = $"root_shop_{suffix}";
        string shopNodeId = $"shop_{suffix}";
        string endNodeId = $"shop_end_{suffix}";

        var pack = new BasePackData
        {
            PackID = $"shop_stage_{suffix}",
            DisplayName = string.IsNullOrWhiteSpace(side?.stageId) ? shop?.name : side.stageId,
            Description = shop != null ? shop.description : string.Empty,
            RootNodeId = rootId,
            Nodes = new Dictionary<string, BaseNodeData>()
        };

        var root = new RootNodeData
        {
            NodeID = rootId,
            Name = "Root",
            EditorPosition = new SerializableVector2(0f, 0f),
            OutputConnections = new List<ConnectionData>(),
            _ = new List<string> { shopNodeId }
        };

        var shopNode = new VFSNodeData
        {
            NodeID = shopNodeId,
            Name = shop != null && !string.IsNullOrWhiteSpace(shop.title) ? shop.title : "Shop",
            Extension = ".shop",
            ContentKind = VFSContentKind.UnityObject,
            ContentSource = VFSContentSource.Reference,
            AssetPath = GetAssetPath(shop),
            ReferencePath = GetResourcesPath(shop),
            UnityObjectTypeName = typeof(ShopSO).AssemblyQualifiedName,
            IsEnabled = true,
            EditorPosition = new SerializableVector2(260f, 0f),
            ParentNodeID = rootId,
            ChildNodeIDs = new List<string> { endNodeId },
            OutputConnections = new List<ConnectionData>()
        };

        var end = new LeafNode_B_Data
        {
            NodeID = endNodeId,
            Name = "End",
            ProcessID = $"shop_{suffix}",
            EditorPosition = new SerializableVector2(520f, 0f),
            InputNodeIDs = new List<string> { shopNodeId },
            OutputConnections = new List<ConnectionData>()
        };

        root.OutputConnections.Add(new ConnectionData(rootId, 0, shopNodeId, 0));
        shopNode.OutputConnections.Add(new ConnectionData(shopNodeId, 0, endNodeId, 0));

        pack.Nodes[rootId] = root;
        pack.Nodes[shopNodeId] = shopNode;
        pack.Nodes[endNodeId] = end;
        ShopResource.RegisterRuntimeShop(shopNodeId, shop);
        return pack;
    }

    private void GenerateEscortLevelsForRoute(BasePackData pack)
    {
        if (pack == null)
            return;

        foreach (var rawNode in pack.Nodes.Values)
        {
            if (rawNode is not VFSNodeData node ||
                !string.Equals(node.Extension, ".stage", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var side = ReadSide(node);
            if (!IsEscortStage(side.stageType))
                continue;

            LevelData level = EscortLevelGenerator.CreateFromRandomClassicMap(BuildEscortRequest(node, side));
            if (level != null)
                _runtimeLevelObjects[node.NodeID] = level;
        }
    }

    private static LevelPlayMode ResolveLevelPlayMode(string stageType)
    {
        if (string.IsNullOrWhiteSpace(stageType))
            return LevelPlayMode.Classic;

        string normalized = stageType.Trim();
        int separator = normalized.IndexOf(':');
        if (separator >= 0)
            normalized = normalized[..separator];

        if (IsEscortStage(normalized))
            return LevelPlayMode.Escort;

        return string.Equals(normalized, "steplimit", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "step_limit", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "step-limit", StringComparison.OrdinalIgnoreCase)
            ? LevelPlayMode.StepLimit
            : LevelPlayMode.Classic;
    }

    private static bool IsEscortStage(string stageType)
    {
        if (string.IsNullOrWhiteSpace(stageType))
            return false;

        string normalized = stageType.Trim();
        int separator = normalized.IndexOf(':');
        if (separator >= 0)
            normalized = normalized[..separator];

        return string.Equals(normalized, "escort", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "escort_ball", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "ball", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "带球", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalized, "押送", StringComparison.OrdinalIgnoreCase);
    }

    private EscortLevelBuildRequest BuildEscortRequest(VFSNodeData node, RunRouteNodeSideData side)
    {
        LevelCollageGenerationSettings settings = ResolveEscortGenerationSettings(node?.NodeID);
        Vector2Int from = ResolveParentRoutePoint(node, side);
        Vector2Int to = new Vector2Int(side.layer, side.lane);
        var context = BuildRouteContext(side);
        int layerUnit = settings != null ? Mathf.Max(1, settings.routeLayerDistanceUnit) : 24;
        int laneUnit = settings != null ? Mathf.Max(1, settings.routeLaneDistanceUnit) : 12;
        int dx = Mathf.Max(1, Mathf.Abs(to.x - from.x)) * layerUnit;
        int dy = Mathf.Abs(to.y - from.y) * laneUnit;
        float slope = dy <= 0 ? 0.001f : dy / (float)dx;
        int rawManhattanDistance = dx + dy;

        return new EscortLevelBuildRequest
        {
            Seed = StableSeed(node?.NodeID),
            ManhattanDistance = settings != null
                ? settings.ClampEscortManhattanDistance(rawManhattanDistance, context)
                : rawManhattanDistance,
            LogSlope = Mathf.Log(slope),
            DifficultyOffset = ResolveEscortDifficultyOffset(side.stageType),
            Context = context,
            Constraints = ResolveEscortConstraints(node?.NodeID, side.stageType, context),
            SourceDatabase = ResolveEscortSourceDatabase(node?.NodeID)
        };
    }

    private static PoolEvalContext BuildRouteContext(RunRouteNodeSideData side)
    {
        var flow = GameFlowController.Instance;
        int routeLayer = Mathf.Max(0, side?.layer ?? 0);
        int routeLayerCount = flow != null ? flow.RouteLayerCount : Mathf.Max(1, routeLayer + 1);
        float progress = routeLayerCount > 1 ? Mathf.Clamp01(routeLayer / (float)(routeLayerCount - 1)) : 0f;
        float difficulty = flow != null ? flow.OverallDifficulty : 1f;

        if (flow != null)
        {
            var snapshot = flow.BuildDifficultySnapshot(routeLayer, routeLayerCount);
            progress = snapshot.Progress;
            difficulty = snapshot.OverallDifficulty;
        }

        return new PoolEvalContext
        {
            routeLayer = routeLayer,
            routeLayerCount = routeLayerCount,
            progress = progress,
            difficulty = Mathf.Max(0f, difficulty),
            seed = StableSeed($"{routeLayer}:{routeLayerCount}:{difficulty:0.###}")
        };
    }

    private LevelCollageSourceDatabase ResolveEscortSourceDatabase(string nodeId)
    {
        return !string.IsNullOrWhiteSpace(nodeId) &&
               _runtimeCollageSettings.TryGetValue(nodeId, out var settings) &&
               settings != null
            ? settings.sourceDatabase
            : null;
    }

    private LevelCollageGenerationSettings ResolveEscortGenerationSettings(string nodeId)
    {
        return !string.IsNullOrWhiteSpace(nodeId) &&
               _runtimeCollageSettings.TryGetValue(nodeId, out var settings) &&
               settings != null
            ? settings
            : null;
    }

    private Vector2Int ResolveParentRoutePoint(VFSNodeData node, RunRouteNodeSideData side)
    {
        var pack = EnsureRoutePack();
        if (pack != null &&
            !string.IsNullOrWhiteSpace(node?.ParentNodeID) &&
            pack.Nodes.TryGetValue(node.ParentNodeID, out var parentRaw) &&
            parentRaw is VFSNodeData parent)
        {
            var parentSide = ReadSide(parent);
            return new Vector2Int(parentSide.layer, parentSide.lane);
        }

        return new Vector2Int(Mathf.Max(0, side.layer - 1), side.lane);
    }

    private static int ResolveEscortDifficultyOffset(string stageType)
    {
        if (string.IsNullOrWhiteSpace(stageType))
            return 0;

        int separator = stageType.IndexOf(':');
        if (separator < 0 || separator >= stageType.Length - 1)
            return 0;

        string token = GetFirstStageParameter(stageType[(separator + 1)..]);
        return token.ToLowerInvariant() switch
        {
            "easy" => -1,
            "normal" => 0,
            "hard" => 1,
            "boss" => 3,
            _ => int.TryParse(token, out int value) ? value : 0
        };
    }

    private EscortLevelGenerationConstraints ResolveEscortConstraints(string nodeId, string stageType, PoolEvalContext context)
    {
        var constraints = !string.IsNullOrWhiteSpace(nodeId) &&
                          _runtimeCollageSettings.TryGetValue(nodeId, out var settings) &&
                          settings != null
            ? settings.ToConstraints(context)
            : EscortLevelGenerationConstraints.Default;
        if (string.IsNullOrWhiteSpace(stageType))
            return constraints;

        int separator = stageType.IndexOf(':');
        if (separator < 0 || separator >= stageType.Length - 1)
            return constraints;

        string[] tokens = stageType[(separator + 1)..].Split(',', ';', '|');
        foreach (string rawToken in tokens)
        {
            string token = rawToken.Trim();
            if (string.IsNullOrWhiteSpace(token))
                continue;

            string lower = token.ToLowerInvariant();
            if (lower is "easy" or "normal" or "hard" or "boss")
                continue;

            if (TryReadIntRange(lower, "eff=", out int minEff, out int maxEff) ||
                TryReadIntRange(lower, "effective=", out minEff, out maxEff))
            {
                constraints.MinTemplateEffectiveBoxes = minEff;
                constraints.MaxTemplateEffectiveBoxes = maxEff;
                continue;
            }

            if (TryReadMaxInt(lower, "eff<=", out int maxOnlyEff))
            {
                constraints.MaxTemplateEffectiveBoxes = maxOnlyEff;
                continue;
            }

            if (TryReadIntRange(lower, "area=", out int minArea, out int maxArea))
            {
                constraints.MinTemplateArea = minArea;
                constraints.MaxTemplateArea = maxArea;
                continue;
            }

            if (TryReadFloatRange(lower, "wall=", out float minWall, out float maxWall))
            {
                constraints.MinTemplateWallRate = minWall;
                constraints.MaxTemplateWallRate = maxWall;
                continue;
            }

            if (TryReadIntRange(lower, "w=", out int minWidth, out int maxWidth) ||
                TryReadIntRange(lower, "width=", out minWidth, out maxWidth))
            {
                constraints.MinTemplateWidth = minWidth;
                constraints.MaxTemplateWidth = maxWidth;
                continue;
            }

            if (TryReadIntRange(lower, "h=", out int minHeight, out int maxHeight) ||
                TryReadIntRange(lower, "height=", out minHeight, out maxHeight))
            {
                constraints.MinTemplateHeight = minHeight;
                constraints.MaxTemplateHeight = maxHeight;
                continue;
            }

            if (TryReadMaxInt(lower, "maxreward=", out int maxReward) ||
                TryReadMaxInt(lower, "maxrewardboxes=", out maxReward))
            {
                constraints.MaxFinalRewardBoxes = maxReward;
            }
        }

        return constraints;
    }

    private static string GetFirstStageParameter(string value)
    {
        string[] tokens = value.Split(',', ';', '|');
        return tokens.Length > 0 ? tokens[0].Trim() : value.Trim();
    }

    private static bool TryReadIntRange(string token, string prefix, out int min, out int max)
    {
        min = 0;
        max = 0;
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string value = token[prefix.Length..].Trim();
        string[] parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out int exact))
        {
            min = exact;
            max = exact;
            return true;
        }

        if (parts.Length == 2 &&
            int.TryParse(parts[0], out min) &&
            int.TryParse(parts[1], out max))
        {
            if (min > max)
                (min, max) = (max, min);

            return true;
        }

        return false;
    }

    private static bool TryReadFloatRange(string token, string prefix, out float min, out float max)
    {
        min = 0f;
        max = 0f;
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string value = token[prefix.Length..].Trim();
        string[] parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && TryParseFloat(parts[0], out float exact))
        {
            min = exact;
            max = exact;
            return true;
        }

        if (parts.Length == 2 &&
            TryParseFloat(parts[0], out min) &&
            TryParseFloat(parts[1], out max))
        {
            if (min > max)
                (min, max) = (max, min);

            return true;
        }

        return false;
    }

    private static bool TryReadMaxInt(string token, string prefix, out int max)
    {
        max = 0;
        return token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(token[prefix.Length..].Trim(), out max);
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static int StableSeed(string value)
    {
        unchecked
        {
            int hash = 17;
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];
            }

            return hash;
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
    [JsonIgnore]
    public LevelCollageGenerationSettings collageGenerationSettings;
    [JsonIgnore]
    public ShopSO shop;
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
