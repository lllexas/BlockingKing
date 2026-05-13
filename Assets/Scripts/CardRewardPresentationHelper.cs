using UnityEngine;

public static class CardRewardPresentationHelper
{
    public static bool TryPlayAddToDeck(CardSO card, int count = 1)
    {
        if (card == null || count <= 0)
            return false;

        var animator = RewardCardPresentationAnimator.ActiveInstance != null
            ? RewardCardPresentationAnimator.ActiveInstance
            : Object.FindObjectOfType<RewardCardPresentationAnimator>(true);

        if (animator == null)
        {
            Debug.LogError($"[CardRewardPresentationHelper] No RewardCardPresentationAnimator found. Add one MonoBehaviour to a persistent visible UI canvas before playing add-to-deck animation. card={card.cardId}, count={count}");
            return false;
        }

        return animator.TryPlayAddToDeck(card, count);
    }
}
