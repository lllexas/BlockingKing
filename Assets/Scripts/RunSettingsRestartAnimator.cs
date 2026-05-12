public sealed class RunSettingsRestartAnimator : MainMenuButtonAnimatorBase
{
    protected override string UIID => RunSettingsUIIds.Restart;

    protected override void Invoke()
    {
        RunSettingsPanelAnimator.Instance?.RestartGame();
    }
}
