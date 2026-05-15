public sealed class MainMenuCampaignAnimator : MainMenuButtonAnimatorBase
{
    protected override string UIID => MainMenuUIIds.Campaign;

    protected override void Invoke()
    {
        Controller?.StartLinearCampaignFromMainMenu();
    }
}
