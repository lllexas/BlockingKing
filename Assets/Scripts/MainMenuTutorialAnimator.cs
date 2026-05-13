public sealed class MainMenuTutorialAnimator : MainMenuButtonAnimatorBase
{
    protected override string UIID => MainMenuUIIds.Tutorial;

    protected override void Invoke()
    {
        Controller?.StartTutorialFromMainMenu();
    }
}
