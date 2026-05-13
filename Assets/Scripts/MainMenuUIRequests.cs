using SpaceTUI;

public static class MainMenuUIIds
{
    public const string Backdrop = "MainMenu.Backdrop";
    public const string Title = "MainMenu.Title";
    public const string Start = "MainMenu.Start";
    public const string Tutorial = "MainMenu.Tutorial";
    public const string Settings = "MainMenu.Settings";
    public const string Quit = "MainMenu.Quit";
}

public sealed class MainMenuUIRequest : IRoutedRequest
{
    public MainMenuUIRequest(string uiid)
    {
        this.uiid = uiid;
    }

    public string uiid { get; }
    public GameFlowController Controller;
    public RunConfigSO RunConfig;
    public bool Instant;
}
