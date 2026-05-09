using System;
using NekoGraph;
using UnityEngine;

[VFSResource(".reward", typeof(RewardSO))]
public static class RewardResource
{
    [VFSExecute]
    public static HandleResult Execute(
        VFSResolvedContent content,
        SignalContext context,
        BasePackData pack,
        GraphRunner runner,
        string packIDKey,
        Action continueAction)
    {
        var reward = content.GetUnityObject<RewardSO>();
        if (reward == null)
        {
            Debug.LogError("[RewardResource] Execute failed: RewardSO is null.");
            return HandleResult.Error;
        }

        switch (reward.rewardKind)
        {
            case RewardSO.RewardKind.AddCardsToDeck:
                return AddCardsToDeck(reward);

            default:
                Debug.LogError($"[RewardResource] Unsupported reward kind: {reward.rewardKind}");
                return HandleResult.Error;
        }
    }

    private static HandleResult AddCardsToDeck(RewardSO reward)
    {
        var deck = GraphHub.Instance?.GetFacade<CardDeckFacade>();
        if (deck == null)
        {
            deck = new CardDeckFacade();
            GraphHub.Instance?.RegisterFacade(deck);
        }

        if (deck == null)
        {
            Debug.LogError("[RewardResource] CardDeckFacade is not available.");
            return HandleResult.Error;
        }

        if (reward.cards == null)
            return HandleResult.Push;

        foreach (var entry in reward.cards)
        {
            if (entry == null || entry.card == null)
                continue;

            int count = Math.Max(1, entry.count);
            if (!deck.AddCard(entry.card, count))
            {
                Debug.LogError($"[RewardResource] Failed to add card reward: {entry.card.name}");
                return HandleResult.Error;
            }
        }

        return HandleResult.Push;
    }
}
