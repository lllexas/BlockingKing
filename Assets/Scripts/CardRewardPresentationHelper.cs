using UnityEngine;

public static class CardRewardPresentationHelper
{
    public static bool TryPlayAddToDeck(CardSO card, int count = 1)
    {
        if (card == null || count <= 0)
            return false;

        var handZone = HandZone.ActiveInstance != null
            ? HandZone.ActiveInstance
            : Object.FindObjectOfType<HandZone>(true);

        if (handZone == null)
            return false;

        return handZone.PlayRewardCardIntoDeck(card, count);
    }
}
