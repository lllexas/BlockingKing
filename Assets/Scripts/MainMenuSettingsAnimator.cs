public sealed class MainMenuSettingsAnimator : MainMenuButtonAnimatorBase
{
    protected override string UIID => MainMenuUIIds.Settings;

    protected override void Invoke()
    {
        RunSettingsPanelAnimator.Instance?.Toggle();
    }
}
