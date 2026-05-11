using SpaceTUI;

public static class RunRoundUIIds
{
    public const string Hud = "RunRoundHud";
    public const string ClassicChoice = "RunRoundClassicChoice";
    public const string EscortChoice = "RunRoundEscortChoice";
    public const string SkipChoice = "RunRoundSkipChoice";
    public const string ShopChoice = "RunRoundShopChoice";
    public const string EventChoice = "RunRoundEventChoice";
    public const string Result = "RunResult";
}

public sealed class RunRoundHudUIRequest : IRoutedRequest
{
    public string uiid => RunRoundUIIds.Hud;
    public RunRoundController Controller;
    public string StatusMessage;
}

public abstract class RunRoundChoiceUIRequest : IRoutedRequest
{
    public RunRoundController Controller;
    public string Title;
    public string Body;
    public string Footer;
    public bool Interactable = true;
    public abstract string uiid { get; }
}

public sealed class RunRoundClassicChoiceUIRequest : RunRoundChoiceUIRequest
{
    public override string uiid => RunRoundUIIds.ClassicChoice;
}

public sealed class RunRoundEscortChoiceUIRequest : RunRoundChoiceUIRequest
{
    public override string uiid => RunRoundUIIds.EscortChoice;
}

public sealed class RunRoundSkipChoiceUIRequest : RunRoundChoiceUIRequest
{
    public override string uiid => RunRoundUIIds.SkipChoice;
}

public sealed class RunRoundShopChoiceUIRequest : RunRoundChoiceUIRequest
{
    public override string uiid => RunRoundUIIds.ShopChoice;
}

public sealed class RunRoundEventChoiceUIRequest : RunRoundChoiceUIRequest
{
    public override string uiid => RunRoundUIIds.EventChoice;
}

public sealed class RunResultUIRequest : IRoutedRequest
{
    public string uiid => RunRoundUIIds.Result;
    public RunRoundController Controller;
    public bool Victory;
    public string Message;
}
