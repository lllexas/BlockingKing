public sealed class RunRoundClassicChoicePanelAnimator : RunRoundChoicePanelAnimatorBase<RunRoundClassicChoiceUIRequest>
{
    protected override string UIID => RunRoundUIIds.ClassicChoice;

    protected override void InvokeChoice(RunRoundController controller)
    {
        controller?.ChooseClassic();
    }
}
