public sealed class RunRoundEscortChoicePanelAnimator : RunRoundChoicePanelAnimatorBase<RunRoundEscortChoiceUIRequest>
{
    protected override string UIID => RunRoundUIIds.EscortChoice;

    protected override void InvokeChoice(RunRoundController controller)
    {
        controller?.ChooseEscort();
    }
}
