public sealed class RunRoundShopChoicePanelAnimator : RunRoundChoicePanelAnimatorBase<RunRoundShopChoiceUIRequest>
{
    protected override string UIID => RunRoundUIIds.ShopChoice;

    protected override void InvokeChoice(RunRoundController controller)
    {
        controller?.ChooseShop();
    }
}
