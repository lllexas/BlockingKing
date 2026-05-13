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

        if (!reward.TryResolveReward(null, out var resolvedReward) || resolvedReward == null)
        {
            Debug.LogWarning("[RewardResource] Reward roll skipped because no reward could be resolved.");
            return HandleResult.Push;
        }

        return ExecuteReward(resolvedReward);
    }

    public static HandleResult ExecuteReward(RewardSO reward)
    {
        switch (reward.rewardKind)
        {
            case RewardSO.RewardKind.AddCardsToDeck:
                return AddCardsToDeck(reward);

            case RewardSO.RewardKind.AddGold:
                return AddGold(reward);

            case RewardSO.RewardKind.AddItem:
                return AddItem(reward);

            case RewardSO.RewardKind.HealRunHp:
                return HealRunHp(reward);

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

    private static HandleResult AddGold(RewardSO reward)
    {
        if (reward.goldAmount <= 0)
            return HandleResult.Push;

        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        if (inventory == null)
        {
            Debug.LogError("[RewardResource] RunInventoryFacade is not available.");
            return HandleResult.Error;
        }

        if (!inventory.AddGold(reward.goldAmount))
        {
            Debug.LogError($"[RewardResource] Failed to add gold reward: {reward.goldAmount}");
            return HandleResult.Error;
        }

        return HandleResult.Push;
    }

    private static HandleResult AddItem(RewardSO reward)
    {
        string itemId = reward.item != null ? reward.item.ResolvedItemId : reward.inventoryItemId;
        string itemType = reward.item != null ? reward.item.itemType : reward.inventoryItemType;
        if (string.IsNullOrWhiteSpace(itemId))
            return HandleResult.Push;

        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        if (inventory == null)
        {
            Debug.LogError("[RewardResource] RunInventoryFacade is not available.");
            return HandleResult.Error;
        }

        if (!inventory.AddItem(itemId, itemType, Math.Max(1, reward.itemCount)))
        {
            Debug.LogError($"[RewardResource] Failed to add item reward: {itemId}");
            return HandleResult.Error;
        }

        return HandleResult.Push;
    }

    private static HandleResult HealRunHp(RewardSO reward)
    {
        if (reward.healAmount <= 0)
            return HandleResult.Push;

        var status = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        if (status == null)
        {
            status = new RunPlayerStatusFacade();
            GraphHub.Instance?.RegisterFacade(status);
        }

        if (status == null)
        {
            Debug.LogError("[RewardResource] RunPlayerStatusFacade is not available.");
            return HandleResult.Error;
        }

        if (!status.Heal(reward.healAmount))
        {
            Debug.LogError($"[RewardResource] Failed to heal run HP: {reward.healAmount}");
            return HandleResult.Error;
        }

        return HandleResult.Push;
    }
}
