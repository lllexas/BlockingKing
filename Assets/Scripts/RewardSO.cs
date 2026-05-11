using System;
using System.Collections.Generic;
using NekoGraph;
using UnityEngine;

[CreateAssetMenu(fileName = "Reward", menuName = "BlockingKing/Run Stage/Reward")]
[VFSContentKind(VFSContentKind.UnityObject)]
public class RewardSO : ScriptableObject
{
    public enum RewardKind
    {
        AddCardsToDeck,
        AddGold
    }

    [Serializable]
    public sealed class CardReward
    {
        public CardSO card;
        public int count = 1;
    }

    [Header("Pool")]
    public RewardPoolSO rewardPool;

    public string rewardId;
    public RewardKind rewardKind = RewardKind.AddCardsToDeck;
    public List<CardReward> cards = new List<CardReward>();

    [Min(0)]
    public int goldAmount;

    public IReadOnlyList<RewardSO> GetRewards()
    {
        if (rewardPool != null && rewardPool.Entries != null && rewardPool.Entries.Count > 0)
        {
            var result = new List<RewardSO>();
            foreach (var entry in rewardPool.Entries)
            {
                if (entry == null || !entry.enabled)
                    continue;

                result.Add(entry.ToReward());
            }

            if (result.Count > 0)
                return result;
        }

        return new List<RewardSO> { this };
    }
}
