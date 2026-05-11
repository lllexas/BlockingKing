using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "RunRewardConfig", menuName = "BlockingKing/Run/Reward Config")]
public class RunRewardConfigSO : ScriptableObject
{
    [Header("Escort")]
    [Min(0)]
    public int escortRewardBoxGold = 2;

    [Min(0)]
    public int escortCompletionGold = 5;

    public List<CardSO> escortCompletionCardRewards = new List<CardSO>();

    public int GetEscortRewardBoxGold(RunDifficultySnapshot difficulty)
    {
        return ScaleGold(escortRewardBoxGold, difficulty.RewardMultiplier);
    }

    public int GetEscortCompletionGold(RunDifficultySnapshot difficulty)
    {
        return ScaleGold(escortCompletionGold, difficulty.RewardMultiplier);
    }

    private static int ScaleGold(int amount, float multiplier)
    {
        if (amount <= 0)
            return 0;

        multiplier = multiplier > 0f ? multiplier : 1f;
        return Mathf.Max(0, Mathf.RoundToInt(amount * multiplier));
    }
}
