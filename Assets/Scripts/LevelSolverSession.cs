using UnityEngine;

public enum LevelSolverVictoryCondition
{
    AllBoxesOnTargets
}

[CreateAssetMenu(fileName = "LevelSolverSession", menuName = "BlockingKing/Tools/Level Solver Session")]
public sealed class LevelSolverSession : ScriptableObject
{
    public LevelData targetLevel;
    public TileMappingConfig config;
    public bool active;

    [Header("Initial Player Stats")]
    public int startingMaxHp = 80;
    public int startingHp = 80;
    public int startingAttack = 4;
    public int startingBlock;

    [Header("Search")]
    public LevelSolverVictoryCondition victoryCondition = LevelSolverVictoryCondition.AllBoxesOnTargets;
    [Min(1)] public int maxDepth = 64;
    [Min(1)] public int maxNodes = 200000;
    public bool includeNoop = true;
    public bool stopOnFirstSolution = true;

    [Header("Output")]
    public string reportPath = "Plan/Active/RuntimeLevelSolverReport.md";
}
