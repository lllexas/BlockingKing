using System;
using System.Collections;
using System.Collections.Generic;
using NekoGraph;
using UnityEngine;

[VFSResource(".msg", typeof(RunMsgSO))]
public static class RunMsgResource
{
    public const string ExecuteEventName = "RunStage.Msg.Execute";
    private static RunMsgPayload _pendingPayload;

    public static bool TryConsumePendingPayload(out RunMsgPayload payload)
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
        var msg = content.GetUnityObject<RunMsgSO>();
        if (msg == null)
        {
            Debug.LogError("[RunMsgResource] Execute failed: RunMsgSO is null.");
            return HandleResult.Error;
        }

        var targets = BuildTargets(content.Node, pack);
        var payload = new RunMsgPayload
        {
            Message = msg,
            PackID = pack?.PackID,
            PackIDKey = packIDKey,
            SourceNodeId = context?.CurrentNodeId,
            SignalId = context?.SignalId,
            Targets = targets
        };

        _pendingPayload = payload;
        RunStagePostLater.SendNextFrame(ExecuteEventName, payload);

        return HandleResult.Wait;
    }

    private static List<RunMsgTarget> BuildTargets(VFSNodeData node, BasePackData pack)
    {
        var targets = new List<RunMsgTarget>();
        if (node == null)
            return targets;

        if (node.ChildNodeIDs != null)
        {
            foreach (var childNodeId in node.ChildNodeIDs)
            {
                AddTarget(targets, pack, childNodeId);
            }
        }

        if (targets.Count == 0 && node.OutputConnections != null)
        {
            foreach (var connection in node.OutputConnections)
            {
                AddTarget(targets, pack, connection.TargetNodeID);
            }
        }

        return targets;
    }

    private static void AddTarget(List<RunMsgTarget> targets, BasePackData pack, string targetNodeId)
    {
        if (string.IsNullOrWhiteSpace(targetNodeId))
            return;

        if (targets.Exists(target => target.TargetNodeId == targetNodeId))
            return;

        targets.Add(new RunMsgTarget
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

public sealed class RunMsgPayload
{
    public RunMsgSO Message;
    public string PackID;
    public string PackIDKey;
    public string SourceNodeId;
    public string SignalId;
    public List<RunMsgTarget> Targets;
}

public sealed class RunMsgTarget
{
    public string TargetNodeId;
    public string TargetName;
}

public static class RunStagePostLater
{
    public static void SendNextFrame(string eventName, object payload)
    {
        if (Application.isPlaying && PostSystem.Instance != null)
        {
            PostSystem.Instance.StartCoroutine(SendNextFrameRoutine(eventName, payload));
            return;
        }

        PostSystem.Instance?.Send(eventName, payload);
    }

    private static IEnumerator SendNextFrameRoutine(string eventName, object payload)
    {
        yield return null;
        PostSystem.Instance?.Send(eventName, payload);
    }
}
