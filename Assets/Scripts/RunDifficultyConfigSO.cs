using System;
using UnityEngine;

[CreateAssetMenu(fileName = "RunDifficultyConfig", menuName = "BlockingKing/Run/Difficulty Config")]
public class RunDifficultyConfigSO : ScriptableObject
{
    [Min(0f)]
    public float overallDifficulty = 1f;

    public EnemySpawnDifficultyProfileSO enemySpawnDifficultyProfile;

    public AnimationCurve enemyHealthMultiplierByProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    public AnimationCurve enemyAttackMultiplierByProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    public AnimationCurve rewardMultiplierByProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    public RunDifficultySnapshot BuildSnapshot(int routeLayer, int routeLayerCount)
    {
        float progress = routeLayerCount > 1
            ? Mathf.Clamp01(routeLayer / (float)(routeLayerCount - 1))
            : 0f;

        return new RunDifficultySnapshot
        {
            Progress = progress,
            OverallDifficulty = Mathf.Max(0f, overallDifficulty),
            EnemyHealthMultiplier = EvaluateMultiplier(enemyHealthMultiplierByProgress, progress),
            EnemyAttackMultiplier = EvaluateMultiplier(enemyAttackMultiplierByProgress, progress),
            RewardMultiplier = EvaluateMultiplier(rewardMultiplierByProgress, progress),
            EnemySpawnDifficultyProfile = enemySpawnDifficultyProfile
        };
    }

    private static float EvaluateMultiplier(AnimationCurve curve, float progress)
    {
        return curve != null ? Mathf.Max(0f, curve.Evaluate(progress)) : 1f;
    }
}

[Serializable]
public struct RunDifficultySnapshot
{
    public float Progress;
    public float OverallDifficulty;
    public float EnemyHealthMultiplier;
    public float EnemyAttackMultiplier;
    public float RewardMultiplier;
    public EnemySpawnDifficultyProfileSO EnemySpawnDifficultyProfile;

    public static RunDifficultySnapshot Default => new RunDifficultySnapshot
    {
        Progress = 0f,
        OverallDifficulty = 1f,
        EnemyHealthMultiplier = 1f,
        EnemyAttackMultiplier = 1f,
        RewardMultiplier = 1f,
        EnemySpawnDifficultyProfile = null
    };
}
