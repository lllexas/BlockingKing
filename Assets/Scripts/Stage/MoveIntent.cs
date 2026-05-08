using UnityEngine;

/// <summary>
/// 单位移动意图。Direction 使用格子方向，Distance 表示推进格数。
/// </summary>
public class MoveIntent : Intent
{
    public Vector2Int Direction;
    public int Distance;

    public void Setup(Vector2Int direction, int distance)
    {
        Direction = direction;
        Distance = distance;
        Active = true;
    }

    public override void Reset()
    {
        base.Reset();
        Direction = Vector2Int.zero;
        Distance = 0;
    }
}
