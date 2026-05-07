using UnityEngine;

/// <summary>
/// 编辑器 → 运行时快速播放桥梁。
/// 放在 Resources/ 下，编辑器设 LevelData 引用 + active=true，
/// 运行时 LevelPlayer 读取后置 active=false。
/// </summary>
[CreateAssetMenu(fileName = "QuickPlaySession", menuName = "BlockingKing/Quick Play Session")]
public class QuickPlaySession : ScriptableObject
{
    public LevelData targetLevel;
    public TileMappingConfig config;
    public bool active;
}
