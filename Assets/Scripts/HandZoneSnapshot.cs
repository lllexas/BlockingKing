using System.Collections.Generic;

public sealed class HandZoneSnapshot
{
    public bool Initialized;
    public int ObservedHandStateRevision;
    public readonly List<CardSO> DrawPile = new();
    public readonly List<CardSO> DiscardPile = new();
    public readonly List<CardSO> Hand = new();
}
