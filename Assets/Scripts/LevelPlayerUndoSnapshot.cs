using System.Collections.Generic;

public sealed class LevelPlayerUndoSnapshot
{
    public bool IsPlaying;
    public bool IsSettled;
    public bool IsStageInputLocked;
    public LevelPlayResult LastResult;
    public int RemainingSteps;
    public string SettlementTitle;
    public string SettlementBody;
    public int SuccessfulBoxCount;
    public readonly List<LevelSettlementRewardLine> RewardLines = new();
}
