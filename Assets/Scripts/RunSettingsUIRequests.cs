using SpaceTUI;

public static class RunSettingsUIIds
{
    public const string Panel = "RunSettings.Panel";
    public const string Restart = "RunSettings.Restart";
    public const string Quit = "RunSettings.Quit";
}

public sealed class RunSettingsPanelUIRequest : IRoutedRequest
{
    public string uiid => RunSettingsUIIds.Panel;
}
