using UnityEngine;

[CreateAssetMenu(fileName = "RunConfig", menuName = "BlockingKing/Run/Run Config")]
public class RunConfigSO : ScriptableObject
{
    [Header("Identity")]
    public string configId;
    public string displayName;

    [Header("Domains")]
    public RunStartSettings startSettings;
    public RunRouteConfigSO routeSettings;
    public RunDifficultyConfigSO difficultySettings;
    public RunRewardConfigSO rewardSettings;
}
