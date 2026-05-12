public sealed class MainMenuStartAnimator : MainMenuButtonAnimatorBase
{
    protected override string UIID => MainMenuUIIds.Start;

    protected override void Invoke()
    {
        Controller?.StartRoundRunFromMainMenu();
    }
}
