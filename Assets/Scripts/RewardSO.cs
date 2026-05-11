using System;
using System.Collections.Generic;
using NekoGraph;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "Reward", menuName = "BlockingKing/Run Stage/Reward")]
[VFSContentKind(VFSContentKind.UnityObject)]
public class RewardSO : ScriptableObject
{
    public enum RewardKind
    {
        AddCardsToDeck,
        AddGold,
        AddItem,
        HealRunHp
    }

    [Serializable]
    public sealed class CardReward
    {
        public CardSO card;
        public int count = 1;
    }

    [Header("Source")]
    [Tooltip("If enabled, this RewardSO rolls one reward from rewardPool instead of applying the direct reward fields below.")]
    public bool rollFromPool;

    [ShowIf(nameof(rollFromPool))]
    [AssetsOnly]
    public RewardPoolSO rewardPool;

    [ShowIf(nameof(ShowDirectRewardFields))]
    public string rewardId;

    [ShowIf(nameof(ShowDirectRewardFields))]
    public RewardKind rewardKind = RewardKind.AddCardsToDeck;

    [ShowIf(nameof(IsCardReward))]
    public List<CardReward> cards = new List<CardReward>();

    [ShowIf(nameof(IsGoldReward))]
    [Min(0)]
    public int goldAmount;

    [ShowIf(nameof(IsHealReward))]
    [Min(1)]
    public int healAmount = 1;

    [ShowIf(nameof(IsItemReward))]
    [AssetsOnly]
    public ItemSO item;

    [ShowIf(nameof(ShowItemFallbackFields))]
    public string inventoryItemId;

    [ShowIf(nameof(ShowItemFallbackFields))]
    public string inventoryItemType;

    [ShowIf(nameof(IsItemReward))]
    [Min(1)]
    public int itemCount = 1;

    private bool IsCardReward => rewardKind == RewardKind.AddCardsToDeck;
    private bool IsGoldReward => rewardKind == RewardKind.AddGold;
    private bool IsItemReward => rewardKind == RewardKind.AddItem;
    private bool IsHealReward => rewardKind == RewardKind.HealRunHp;
    private bool ShowItemFallbackFields => IsItemReward && item == null;
    private bool ShowDirectRewardFields => !rollFromPool;

    public bool TryResolveReward(System.Random random, out RewardSO reward)
    {
        reward = this;
        if (!rollFromPool)
            return true;

        if (rewardPool == null)
        {
            reward = null;
            return false;
        }

        return rewardPool.TryRollReward(random, out reward);
    }
}
