using UnityEngine;

[CreateAssetMenu(fileName = "RunRewardConfig", menuName = "BlockingKing/Run/Reward Config")]
public class RunRewardConfigSO : ScriptableObject
{
    [Header("Classic")]
    [Min(0)]
    public int classicPhaseOneFirstBoxGold = 3;

    [Min(0)]
    public int classicPhaseOneBoxGoldStep = 3;

    [Min(0)]
    public int classicPhaseTwoFirstBoxGold = 3;

    [Min(0)]
    public int classicPhaseTwoBoxGoldStep = 1;

    [Header("Escort")]
    [Min(0)]
    public int escortRewardBoxGold = 2;

    [Min(0)]
    public int escortCompletionGold = 5;

    public RewardPoolSO escortCompletionRewardPool;

    public int GetEscortRewardBoxGold(RunDifficultySnapshot difficulty)
    {
        return ScaleGold(escortRewardBoxGold, difficulty.RewardMultiplier);
    }

    public int GetEscortCompletionGold(RunDifficultySnapshot difficulty)
    {
        return ScaleGold(escortCompletionGold, difficulty.RewardMultiplier);
    }

    public int GetClassicPhaseOneBoxGold(int count, RunDifficultySnapshot difficulty)
    {
        return ScaleGold(CalculateArithmeticSequence(count, classicPhaseOneFirstBoxGold, classicPhaseOneBoxGoldStep), difficulty.RewardMultiplier);
    }

    public int GetClassicPhaseTwoBoxGold(int count, RunDifficultySnapshot difficulty)
    {
        return ScaleGold(CalculateArithmeticSequence(count, classicPhaseTwoFirstBoxGold, classicPhaseTwoBoxGoldStep), difficulty.RewardMultiplier);
    }

    public int GetClassicPhaseOnePreviewGold(int count)
    {
        return CalculateArithmeticSequence(count, classicPhaseOneFirstBoxGold, classicPhaseOneBoxGoldStep);
    }

    public int GetClassicPhaseTwoPreviewGold(int count)
    {
        return CalculateArithmeticSequence(count, classicPhaseTwoFirstBoxGold, classicPhaseTwoBoxGoldStep);
    }

    public static int CalculateArithmeticSequence(int count, int firstAmount, int stepAmount)
    {
        if (count <= 0)
            return 0;

        int amount = 0;
        for (int i = 0; i < count; i++)
            amount += firstAmount + i * stepAmount;

        return amount;
    }

    private static int ScaleGold(int amount, float multiplier)
    {
        if (amount <= 0)
            return 0;

        multiplier = multiplier > 0f ? multiplier : 1f;
        return Mathf.Max(0, Mathf.RoundToInt(amount * multiplier));
    }
}
