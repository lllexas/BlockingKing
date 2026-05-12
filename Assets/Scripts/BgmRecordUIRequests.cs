using SpaceTUI;

public static class BgmRecordUIIds
{
    public const string RecordButton = "BgmRecord.RecordButton";
}

public sealed class BgmRecordUIRequest : IRoutedRequest
{
    public BgmRecordUIRequest(string uiid)
    {
        this.uiid = uiid;
    }

    public string uiid { get; }
}
