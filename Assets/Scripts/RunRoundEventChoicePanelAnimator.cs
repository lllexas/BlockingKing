public sealed class RunRoundEventChoicePanelAnimator : RunRoundChoicePanelAnimatorBase<RunRoundEventChoiceUIRequest>
{
    protected override string UIID => RunRoundUIIds.EventChoice;

    protected override void InvokeChoice(RunRoundController controller)
    {
        controller?.ChooseEvent();
    }
}
