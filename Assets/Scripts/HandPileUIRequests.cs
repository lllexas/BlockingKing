using SpaceTUI;

public static class HandPileUIIds
{
    public const string DrawPile = "HandPile.Draw";
    public const string DiscardPile = "HandPile.Discard";
}

public sealed class HandPileUIRequest : IRoutedRequest
{
    public HandPileUIRequest(string uiid, int count)
    {
        this.uiid = uiid;
        Count = count;
    }

    public string uiid { get; }
    public int Count { get; }
}
