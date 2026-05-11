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
        public string rewardId;
        public RewardSO.RewardKind rewardKind = RewardSO.RewardKind.AddCardsToDeck;

        [Serializable]
        public sealed class CardRewardEntry
        {
            public CardSO card;
            public int count = 1;
        }

        public List<CardRewardEntry> cards = new List<CardRewardEntry>();

        [Min(0)]
        public int goldAmount;

        public RewardSO ToReward()
        {
            var reward = ScriptableObject.CreateInstance<RewardSO>();
            reward.rewardId = rewardId;
            reward.rewardKind = rewardKind;
            reward.goldAmount = goldAmount;

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

    public List<Entry> entries = new List<Entry>();

    public override IReadOnlyList<Entry> Entries => entries;

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
            _ => $"Reward {index}"
        };
    }
}
