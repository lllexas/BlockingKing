using SpaceTUI;

public static class TutorialUIIds
{
    public const string Prompt = "TutorialPrompt";
}

public sealed class TutorialPromptUIRequest : IRoutedRequest
{
    public string uiid => TutorialUIIds.Prompt;
    public string Title;
    public string Message;
}
