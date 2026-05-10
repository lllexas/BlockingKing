using UnityEngine;

public class SpawnIntent : Intent
{
    public Vector2Int Origin;
    public EntityBP EntityBP;

    public void Setup(Vector2Int origin, EntityBP entityBP)
    {
        Origin = origin;
        EntityBP = entityBP;
        Active = true;
    }

    public override void Reset()
    {
        base.Reset();
        Origin = Vector2Int.zero;
        EntityBP = null;
    }
}
