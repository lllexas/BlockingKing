public sealed class RunRoundSkipChoicePanelAnimator : RunRoundChoicePanelAnimatorBase<RunRoundSkipChoiceUIRequest>
{
    protected override string UIID => RunRoundUIIds.SkipChoice;

    protected override void InvokeChoice(RunRoundController controller)
    {
        controller?.SkipRound();
    }
}
