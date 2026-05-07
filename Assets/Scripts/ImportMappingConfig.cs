using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

/// <summary>
/// 经典 Sokoban 文本关卡 → LevelData SO 的符号映射配置。
/// 一份 SO = 一组映射，支持多组配置应对不同关卡格式。
/// </summary>
[CreateAssetMenu(fileName = "ImportMappingConfig", menuName = "BlockingKing/Import Mapping Config")]
public class ImportMappingConfig : ScriptableObject
{
    [BoxGroup("地形符号")]
    [Tooltip("墙壁字符，通常为 #")]
    public char wallChar = '#';
    [BoxGroup("地形符号")]
    [Tooltip("墙壁对应的 terrain ID")]
    public int wallTerrainID = 2;

    [BoxGroup("地形符号")]
    [Tooltip("地板字符，通常为空格")]
    public char floorChar = ' ';
    [BoxGroup("地形符号")]
    [Tooltip("地板对应的 terrain ID")]
    public int floorTerrainID = 1;

    [BoxGroup("Tag 符号")]
    [Tooltip("箱子字符，通常为 $")]
    public char boxChar = '$';
    [BoxGroup("Tag 符号")]
    [Tooltip("箱子对应的 tag ID")]
    public int boxTagID = 2;

    [BoxGroup("Tag 符号")]
    [Tooltip("玩家字符，通常为 @")]
    public char playerChar = '@';
    [BoxGroup("Tag 符号")]
    [Tooltip("玩家对应的 tag ID")]
    public int playerTagID = 3;

    [BoxGroup("Tag 符号")]
    [Tooltip("目标点字符，通常为 .")]
    public char targetChar = '.';
    [BoxGroup("Tag 符号")]
    [Tooltip("目标点对应的 tag ID")]
    public int targetTagID = 1;

    [BoxGroup("复合符号拆分")]
    [Tooltip("箱子在目标上，通常为 * → 拆成 targetTag + boxTag")]
    public char boxOnTargetChar = '*';

    [BoxGroup("复合符号拆分")]
    [Tooltip("玩家在目标上，通常为 + → 拆成 targetTag + playerTag")]
    public char playerOnTargetChar = '+';

    // ─────────── 查询 ───────────

    /// <summary>字符对应的 terrain ID，非地形返回 -1</summary>
    public int GetTerrainID(char c)
    {
        if (c == wallChar) return wallTerrainID;
        if (c == floorChar) return floorTerrainID;
        return -1;
    }

    /// <summary>字符对应的 tag 列表。复合符号（* +）拆成多条。</summary>
    public List<int> GetTagIDs(char c)
    {
        if (c == boxChar) return new List<int> { boxTagID };
        if (c == playerChar) return new List<int> { playerTagID };
        if (c == targetChar) return new List<int> { targetTagID };
        if (c == boxOnTargetChar) return new List<int> { targetTagID, boxTagID };
        if (c == playerOnTargetChar) return new List<int> { targetTagID, playerTagID };
        return null;
    }

    /// <summary>是否是地形字符</summary>
    public bool IsTerrainChar(char c) => c == wallChar || c == floorChar;

    /// <summary>是否有 Tag 的字符（包括复合）</summary>
    public bool HasTagChar(char c)
    {
        return c == boxChar || c == playerChar || c == targetChar
            || c == boxOnTargetChar || c == playerOnTargetChar;
    }
}
