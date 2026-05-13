using UnityEngine;

/// <summary>
/// Editor -> Play Mode bridge for level editing.
/// Runtime reads this once when GameFlowMode.LevelEdit starts.
/// </summary>
[CreateAssetMenu(fileName = "LevelEditSession", menuName = "BlockingKing/Level Edit Session")]
public class LevelEditSession : ScriptableObject
{
    public LevelData targetLevel;
    public TileMappingConfig config;
    public bool active;
}
