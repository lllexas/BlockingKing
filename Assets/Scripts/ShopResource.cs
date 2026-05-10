using System;
using System.Collections.Generic;
using NekoGraph;
using UnityEngine;

[VFSResource(".shop", typeof(ShopSO))]
public static class ShopResource
{
    public const string ExecuteEventName = "RunStage.Shop.Execute";
    private static ShopPayload _pendingPayload;
    private static readonly Dictionary<string, ShopSO> RuntimeShops = new Dictionary<string, ShopSO>();

    public static void RegisterRuntimeShop(string nodeId, ShopSO shop)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || shop == null)
            return;

        RuntimeShops[nodeId] = shop;
    }

    public static bool TryConsumePendingPayload(out ShopPayload payload)
    {
        payload = _pendingPayload;
        _pendingPayload = null;
        return payload != null;
    }

    [VFSExecute]
    public static HandleResult Execute(
        VFSResolvedContent content,
        SignalContext context,
        BasePackData pack,
        GraphRunner runner,
        string packIDKey,
        Action continueAction)
    {
        var shop = content.GetUnityObject<ShopSO>();
        if (shop == null &&
            content.Node != null &&
            RuntimeShops.TryGetValue(content.Node.NodeID, out var runtimeShop))
        {
            shop = runtimeShop;
        }

        if (shop == null)
        {
            Debug.LogError("[ShopResource] Execute failed: ShopSO is null.");
            return HandleResult.Error;
        }

        var payload = new ShopPayload
        {
            Shop = shop,
            PackID = pack?.PackID,
            PackIDKey = packIDKey,
            SourceNodeId = context?.CurrentNodeId,
            SignalId = context?.SignalId,
            Targets = BuildTargets(content.Node, pack)
        };

        _pendingPayload = payload;
        RunStagePostLater.SendNextFrame(ExecuteEventName, payload);

        return HandleResult.Wait;
    }

    private static List<ShopTarget> BuildTargets(VFSNodeData node, BasePackData pack)
    {
        var targets = new List<ShopTarget>();
        if (node == null)
            return targets;

        if (node.ChildNodeIDs != null)
        {
            foreach (var childNodeId in node.ChildNodeIDs)
                AddTarget(targets, pack, childNodeId);
        }

        if (targets.Count == 0 && node.OutputConnections != null)
        {
            foreach (var connection in node.OutputConnections)
                AddTarget(targets, pack, connection.TargetNodeID);
        }

        return targets;
    }

    private static void AddTarget(List<ShopTarget> targets, BasePackData pack, string targetNodeId)
    {
        if (string.IsNullOrWhiteSpace(targetNodeId))
            return;

        if (targets.Exists(target => target.TargetNodeId == targetNodeId))
            return;

        targets.Add(new ShopTarget
        {
            TargetNodeId = targetNodeId,
            TargetName = pack != null &&
                         pack.Nodes != null &&
                         pack.Nodes.TryGetValue(targetNodeId, out var node)
                ? node.Name
                : string.Empty
        });
    }
}

public sealed class ShopPayload
{
    public ShopSO Shop;
    public string PackID;
    public string PackIDKey;
    public string SourceNodeId;
    public string SignalId;
    public List<ShopTarget> Targets;
}

public sealed class ShopTarget
{
    public string TargetNodeId;
    public string TargetName;
}
