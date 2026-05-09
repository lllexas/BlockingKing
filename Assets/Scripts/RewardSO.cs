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
        AddCardsToDeck
    }

    [Serializable]
    public sealed class CardReward
    {
        public CardSO card;
        public int count = 1;
    }

    public string rewardId;
    public RewardKind rewardKind = RewardKind.AddCardsToDeck;
    public List<CardReward> cards = new List<CardReward>();
}
