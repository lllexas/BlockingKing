using UnityEngine;

/// <summary>
/// 卡牌打出意图。
/// 先把卡牌写入 intent，再由 CardEffectSystem 统一解析和执行。
/// </summary>
public class CardIntent : Intent
{
    public CardSO Card;
    public Vector2Int PlayerCell;
    public Vector2Int TargetCell;
    public Vector2Int Direction;
    public bool HasDirection;

    public void Setup(CardSO card, CardReleaseTarget target)
    {
        Card = card;
        PlayerCell = target.PlayerCell;
        TargetCell = target.TargetCell;
        Direction = target.Direction;
        HasDirection = target.HasDirection;
        Active = true;
    }

    public override void Reset()
    {
        base.Reset();
        Card = null;
        PlayerCell = Vector2Int.zero;
        TargetCell = Vector2Int.zero;
        Direction = Vector2Int.zero;
        HasDirection = false;
    }
}
