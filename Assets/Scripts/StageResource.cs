using System;
using NekoGraph;
using UnityEngine;

[VFSResource(".stage")]
public static class StageResource
{
    public const string ExecuteEventName = "RunStage.Stage.Execute";

    [VFSExecute]
    public static HandleResult Execute(
        VFSResolvedContent content,
        SignalContext context,
        BasePackData pack,
        GraphRunner runner,
        string packIDKey,
        Action continueAction)
    {
        if (content == null)
        {
            Debug.LogError("[StageResource] Execute failed: content is null.");
            return HandleResult.Error;
        }

        if (TryExecuteLevelData(content, context, pack, packIDKey))
            return HandleResult.Wait;

        if (TryExecutePack(content, context, pack, packIDKey))
            return HandleResult.Wait;

        Debug.LogError($"[StageResource] Unsupported .stage content kind: {content.Kind}");
        return HandleResult.Error;
    }

    private static bool TryExecuteLevelData(
        VFSResolvedContent content,
        SignalContext context,
        BasePackData pack,
        string packIDKey)
    {
        var level = content.GetUnityObject<LevelData>();
        if (level == null)
            return false;

        RegisterWaitingStage(pack, context);

        PostSystem.Instance.Send(ExecuteEventName, new StagePayload
        {
            Level = level,
            PackID = pack?.PackID,
            PackIDKey = packIDKey,
            SourceNodeId = context?.CurrentNodeId,
            SignalId = context?.SignalId
        });

        var player = UnityEngine.Object.FindObjectOfType<LevelPlayer>();
        if (player != null)
        {
            player.PlayLevel(level);
        }
        else
        {
            Debug.LogWarning("[StageResource] LevelPlayer not found. Stage payload was posted only.");
        }

        return true;
    }

    private static bool TryExecutePack(VFSResolvedContent content, SignalContext context, BasePackData pack, string packIDKey)
    {
        var stagePack = content.GetNekographPack();
        if (stagePack == null)
            return false;

        var facade = GraphHub.Instance?.GetFacade<RunStageFacade>();
        if (facade == null)
        {
            facade = new RunStageFacade();
            GraphHub.Instance?.RegisterFacade(facade);
        }

        if (facade == null)
        {
            Debug.LogError("[StageResource] RunStageFacade is not available.");
            return true;
        }

        RegisterWaitingStage(pack, context);

        PostSystem.Instance.Send(ExecuteEventName, new StagePayload
        {
            StagePack = stagePack,
            PackID = pack?.PackID,
            PackIDKey = packIDKey,
            SourceNodeId = context?.CurrentNodeId,
            SignalId = context?.SignalId
        });

        facade.TryRunStagePack(stagePack, unloadPrevious: false, args: context?.Args);
        return true;
    }

    private static void RegisterWaitingStage(BasePackData pack, SignalContext context)
    {
        if (pack == null || context == null)
            return;

        var facade = GraphHub.Instance?.GetFacade<RunStageFacade>();
        if (facade == null)
        {
            facade = new RunStageFacade();
            GraphHub.Instance?.RegisterFacade(facade);
        }

        facade?.SetWaitingStage(pack.PackID, context.SignalId);
    }
}

public sealed class StagePayload
{
    public LevelData Level;
    public BasePackData StagePack;
    public string PackID;
    public string PackIDKey;
    public string SourceNodeId;
    public string SignalId;
}
