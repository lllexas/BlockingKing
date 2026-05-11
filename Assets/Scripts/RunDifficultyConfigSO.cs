using System;
using UnityEngine;

[CreateAssetMenu(fileName = "RunDifficultyConfig", menuName = "BlockingKing/Run/Difficulty Config")]
public class RunDifficultyConfigSO : ScriptableObject
{
    [Min(0f)]
    public float overallDifficulty = 1f;

    public EnemySpawnDifficultyProfileSO enemySpawnDifficultyProfile;
    public EnemySpawnTimingProfileSO enemySpawnTimingProfile;

    public AnimationCurve enemyHealthMultiplierByProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    public AnimationCurve enemyAttackMultiplierByProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    public AnimationCurve rewardMultiplierByProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    public RunDifficultySnapshot BuildSnapshot(int routeLayer, int routeLayerCount)
    {
        float progress = CalculateProgress(routeLayer, routeLayerCount);

        return new RunDifficultySnapshot
        {
            Progress = progress,
            OverallDifficulty = Mathf.Max(0f, overallDifficulty),
            EnemyHealthMultiplier = EvaluateMultiplier(enemyHealthMultiplierByProgress, progress),
            EnemyAttackMultiplier = EvaluateMultiplier(enemyAttackMultiplierByProgress, progress),
            RewardMultiplier = EvaluateMultiplier(rewardMultiplierByProgress, progress),
            EnemySpawnDifficultyProfile = enemySpawnDifficultyProfile,
            EnemySpawnTimingProfile = enemySpawnTimingProfile
        };
    }

    private static float EvaluateMultiplier(AnimationCurve curve, float progress)
    {
        return curve != null ? Mathf.Max(0f, curve.Evaluate(progress)) : 1f;
    }

    public static float CalculateProgress(int routeLayer, int routeLayerCount)
    {
        routeLayerCount = Mathf.Max(1, routeLayerCount);
        if (routeLayerCount <= 1)
            return 0f;

        int zeroBasedLayer = routeLayer >= 1 && routeLayer <= routeLayerCount
            ? routeLayer - 1
            : routeLayer;

        return Mathf.Clamp01(zeroBasedLayer / (float)(routeLayerCount - 1));
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
    public EnemySpawnTimingProfileSO EnemySpawnTimingProfile;

    public static RunDifficultySnapshot Default => new RunDifficultySnapshot
    {
        Progress = 0f,
        OverallDifficulty = 1f,
        EnemyHealthMultiplier = 1f,
        EnemyAttackMultiplier = 1f,
        RewardMultiplier = 1f,
        EnemySpawnDifficultyProfile = null,
        EnemySpawnTimingProfile = null
    };
}
