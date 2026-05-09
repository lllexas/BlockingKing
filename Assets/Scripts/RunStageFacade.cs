using NekoGraph;
using UnityEngine;

/// <summary>
/// 一个 .nekograph 表示一次游戏过程中的一个关卡节点：
/// 战斗、奖励、随机事件等都通过加载该 Pack 并交给 GraphRunner 执行。
/// </summary>
public class RunStageFacade : PackFacadeBase
{
    public const string DefaultPackID = "run_stage";

    public string LoadedPackID { get; private set; }
    public BasePackData LoadedPack { get; private set; }
    public int LoadedStageRunVersion { get; private set; }
    public string WaitingStagePackID { get; private set; }
    public string WaitingStageSignalID { get; private set; }

    protected override string GetDefaultPackID()
    {
        return DefaultPackID;
    }

    public bool TryRunStage(TextAsset nekographAsset, bool unloadPrevious = true, object args = null)
    {
        if (nekographAsset == null)
        {
            Debug.LogError("[RunStageFacade] .nekograph asset is null.");
            return false;
        }

        return TryRunStageJson(nekographAsset.text, unloadPrevious, args);
    }

    public bool TryRunStageJson(string json, bool unloadPrevious = true, object args = null)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("[RunStageFacade] .nekograph json is empty.");
            return false;
        }

        BasePackData pack;
        try
        {
            pack = BasePackData.FromJson(json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[RunStageFacade] Failed to parse .nekograph: {e.Message}");
            return false;
        }

        return TryRunStagePack(pack, unloadPrevious, args);
    }

    public bool TryRunStagePack(BasePackData pack, bool unloadPrevious = true, object args = null)
    {
        if (pack == null)
        {
            Debug.LogError("[RunStageFacade] Pack is null.");
            return false;
        }

        var runner = GraphHub.Instance?.DefaultRunner;
        if (runner == null)
        {
            Debug.LogError("[RunStageFacade] GraphRunner is not available.");
            return false;
        }

        if (unloadPrevious && !string.IsNullOrEmpty(LoadedPackID))
        {
            runner.UnloadPack(LoadedPackID);
        }

        var packID = runner.LoadPack(pack);
        if (string.IsNullOrEmpty(packID))
        {
            return false;
        }

        LoadedPackID = packID;
        LoadedPack = pack;
        LoadedStageRunVersion++;
        runner.InjectSignalFromRoot(packID, args);
        return true;
    }

    public bool IsLoadedStageComplete()
    {
        if (string.IsNullOrWhiteSpace(LoadedPackID) || LoadedPack == null)
            return false;

        var runner = GraphHub.Instance?.DefaultRunner;
        if (runner?.PackTable == null ||
            !runner.PackTable.TryGetValue(LoadedPackID, out var runtimePack))
        {
            return true;
        }

        if (runtimePack == null)
            return true;

        return runtimePack.ActiveSignals.Count == 0 &&
               (runtimePack.SuspendedSignals == null || runtimePack.SuspendedSignals.Count == 0);
    }

    public void ClearLoadedStage(bool unloadPack = true)
    {
        if (unloadPack && !string.IsNullOrWhiteSpace(LoadedPackID))
            GraphHub.Instance?.DefaultRunner?.UnloadPack(LoadedPackID);

        LoadedPackID = null;
        LoadedPack = null;
    }

    public void SetWaitingStage(string packID, string signalID)
    {
        WaitingStagePackID = packID;
        WaitingStageSignalID = signalID;
    }

    public void ClearWaitingStage()
    {
        WaitingStagePackID = null;
        WaitingStageSignalID = null;
    }

    public bool HasWaitingStage()
    {
        return !string.IsNullOrWhiteSpace(WaitingStagePackID) &&
               !string.IsNullOrWhiteSpace(WaitingStageSignalID);
    }

    public bool ResumeWaitingStage(int targetIndex = 0)
    {
        if (!TryGetWaitingStageTarget(targetIndex, out var sourceNodeID, out var targetNodeID))
            return false;

        var runner = GraphHub.Instance?.DefaultRunner;
        if (runner == null)
            return false;

        bool resumed = runner.ResumeSuspendedSignalToTarget(
            WaitingStagePackID,
            WaitingStageSignalID,
            sourceNodeID,
            targetNodeID);

        if (resumed)
            ClearWaitingStage();

        return resumed;
    }

    private bool TryGetWaitingStageTarget(int targetIndex, out string sourceNodeID, out string targetNodeID)
    {
        sourceNodeID = null;
        targetNodeID = null;

        if (!HasWaitingStage())
            return false;

        var runner = GraphHub.Instance?.DefaultRunner;
        if (runner == null ||
            runner.PackTable == null ||
            !runner.PackTable.TryGetValue(WaitingStagePackID, out var pack) ||
            pack == null ||
            pack.SuspendedSignals == null ||
            !pack.SuspendedSignals.TryGetValue(WaitingStageSignalID, out var signal) ||
            signal == null ||
            string.IsNullOrWhiteSpace(signal.CurrentNodeId) ||
            pack.Nodes == null ||
            !pack.Nodes.TryGetValue(signal.CurrentNodeId, out var node))
        {
            return false;
        }

        sourceNodeID = signal.CurrentNodeId;

        if (node is VFSNodeData vfsNode && vfsNode.ChildNodeIDs != null && targetIndex >= 0 && targetIndex < vfsNode.ChildNodeIDs.Count)
        {
            targetNodeID = vfsNode.ChildNodeIDs[targetIndex];
            return !string.IsNullOrWhiteSpace(targetNodeID);
        }

        if (node.OutputConnections != null && targetIndex >= 0 && targetIndex < node.OutputConnections.Count)
        {
            targetNodeID = node.OutputConnections[targetIndex].TargetNodeID;
            return !string.IsNullOrWhiteSpace(targetNodeID);
        }

        return false;
    }
}
