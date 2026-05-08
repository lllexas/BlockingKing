/// <summary>
/// 单位攻击意图。当前先只记录目标，结算由 AttackSystem 后续接管。
/// </summary>
public class AttackIntent : Intent
{
    public EntityHandle Target;

    public void Setup(EntityHandle target)
    {
        Target = target;
        Active = true;
    }

    public override void Reset()
    {
        base.Reset();
        Target = EntityHandle.None;
    }
}
