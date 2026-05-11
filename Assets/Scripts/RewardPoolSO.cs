using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "RewardPool", menuName = "BlockingKing/Pool/Reward Pool")]
public class RewardPoolSO : ContentPoolSO<RewardPoolSO.Entry, RewardSO>
{
    [Serializable]
    public sealed class Entry : PoolEntryBase
    {
        [TableColumnWidth(110)]
        public string rewardId;

        [TableColumnWidth(92)]
        public RewardSO.RewardKind rewardKind = RewardSO.RewardKind.AddCardsToDeck;

        [Serializable]
        public sealed class CardRewardEntry
        {
            [TableColumnWidth(120)]
            [AssetsOnly]
            public CardSO card;

            [TableColumnWidth(70)]
            [Min(1)]
            public int count = 1;
        }

        [ShowIf(nameof(IsCardReward))]
        [TableList(AlwaysExpanded = true, DrawScrollView = false)]
        public List<CardRewardEntry> cards = new List<CardRewardEntry>();

        [ShowIf(nameof(IsGoldReward))]
        [TableColumnWidth(80)]
        [Min(0)]
        public int goldAmount;

        [ShowIf(nameof(IsHealReward))]
        [TableColumnWidth(80)]
        [Min(1)]
        public int healAmount = 1;

        [ShowIf(nameof(IsItemReward))]
        [TableColumnWidth(120)]
        [AssetsOnly]
        public ItemSO item;

        [ShowIf(nameof(IsItemReward))]
        [TableColumnWidth(70)]
        [Min(1)]
        public int itemCount = 1;

        private bool IsCardReward => rewardKind == RewardSO.RewardKind.AddCardsToDeck;
        private bool IsGoldReward => rewardKind == RewardSO.RewardKind.AddGold;
        private bool IsItemReward => rewardKind == RewardSO.RewardKind.AddItem;
        private bool IsHealReward => rewardKind == RewardSO.RewardKind.HealRunHp;

        public RewardSO ToReward()
        {
            var reward = ScriptableObject.CreateInstance<RewardSO>();
            reward.rewardId = rewardId;
            reward.rewardKind = rewardKind;
            reward.goldAmount = goldAmount;
            reward.healAmount = healAmount;
            reward.item = item;
            reward.itemCount = itemCount;

            if (cards != null)
            {
                foreach (var entry in cards)
                {
                    if (entry == null)
                        continue;

                    reward.cards.Add(new RewardSO.CardReward
                    {
                        card = entry.card,
                        count = entry.count
                    });
                }
            }

            return reward;
        }
    }

    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 160)]
    public List<Entry> entries = new List<Entry>();

    public override IReadOnlyList<Entry> Entries => entries;

    public bool TryRollReward(System.Random random, out RewardSO reward)
    {
        reward = Roll(
            random,
            entry => entry != null,
            entry => entry.ToReward());

        return reward != null;
    }

    protected override string GetEntryDisplayName(Entry entry, int index)
    {
        if (entry == null)
            return base.GetEntryDisplayName(entry, index);

        if (!string.IsNullOrWhiteSpace(entry.rewardId))
            return entry.rewardId;

        return entry.rewardKind switch
        {
            RewardSO.RewardKind.AddCardsToDeck => $"Card Reward {index}",
            RewardSO.RewardKind.AddGold => $"Gold {entry.goldAmount}",
            RewardSO.RewardKind.AddItem => entry.item != null ? entry.item.ResolvedDisplayName : $"Item Reward {index}",
            RewardSO.RewardKind.HealRunHp => $"Heal {entry.healAmount} HP",
            _ => $"Reward {index}"
        };
    }
}
