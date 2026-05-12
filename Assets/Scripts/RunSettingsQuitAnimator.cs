public sealed class RunSettingsQuitAnimator : MainMenuButtonAnimatorBase
{
    protected override string UIID => RunSettingsUIIds.Quit;

    protected override void Invoke()
    {
        RunSettingsPanelAnimator.Instance?.QuitGame();
    }
}
