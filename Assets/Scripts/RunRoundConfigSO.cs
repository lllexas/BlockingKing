using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "RunRoundConfig", menuName = "BlockingKing/Run/Round Config")]
public sealed class RunRoundConfigSO : ScriptableObject
{
    [Header("Round Flow")]
    [Min(1)]
    public int totalRounds = 12;

    [Min(1)]
    public int seed = 1;

    [Header("State A - Main Combat Choices")]
    public LevelCollageSourceDatabase levelSourceDatabase;

    [FormerlySerializedAs("classicSelectionTable")]
    public LevelFeatureSelectionTableSO classicFeatureSelectionTable;

    public LevelFeatureSelectionTableSO escortFeatureSelectionTable;

    public RewardPoolSO skipRewardPool;

    [FormerlySerializedAs("alternateModeBonusGold")]
    [Tooltip("Gold granted after completing a main combat mode different from the previously completed main combat mode. Example: Classic then Escort, or Escort then Classic.")]
    [Min(0)]
    public int classicEscortAlternationGold = 3;

    [Header("State B - After Combat Choices")]
    public ShopItemPoolSO shopItemPool;

    public EventStagePoolSO eventStagePool;

    [Header("Legacy Adapters")]
    [FormerlySerializedAs("escortGenerationSettings")]
    public LevelCollageGenerationSettings legacyEscortGenerationSettings;
}
