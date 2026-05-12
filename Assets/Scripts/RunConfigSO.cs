using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "RunConfig", menuName = "BlockingKing/Run/Run Config")]
public class RunConfigSO : ScriptableObject
{
    [Header("Identity")]
    public string configId;
    public string displayName;

    [Header("Domains")]
    public RunStartSettings startSettings;
    public RunRouteConfigSO routeSettings;
    [FormerlySerializedAs("roundFlowSettings")]
    public RunRoundConfigSO roundSettings;
    public RunDifficultyConfigSO difficultySettings;
    public RunRewardConfigSO rewardSettings;
    public BgmPromptSO mainMenuBgm;
    public BgmPlaylistSO bgmPlaylist;
}
