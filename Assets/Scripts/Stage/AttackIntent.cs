using UnityEngine;

/// <summary>
/// 单位攻击意图。记录本次攻击覆盖的格子和对应伤害倍率。
/// </summary>
public class AttackIntent : Intent
{
    private const int DefaultCapacity = 8;

    public Vector2Int[] TargetPositions = new Vector2Int[DefaultCapacity];
    public float[] DamageMultipliers = new float[DefaultCapacity];
    public int TargetCount;

    public void Setup(Vector2Int targetPosition)
    {
        ClearTargets();
        AddTarget(targetPosition, 1f);
        Active = true;
    }

    public void AddTarget(Vector2Int targetPosition, float damageMultiplier = 1f)
    {
        EnsureCapacity(TargetCount + 1);
        TargetPositions[TargetCount] = targetPosition;
        DamageMultipliers[TargetCount] = damageMultiplier;
        TargetCount++;
    }

    public override void Reset()
    {
        base.Reset();
        ClearTargets();
    }

    private void ClearTargets()
    {
        TargetCount = 0;
    }

    private void EnsureCapacity(int capacity)
    {
        if (TargetPositions.Length >= capacity)
            return;

        int newCapacity = TargetPositions.Length;
        while (newCapacity < capacity)
            newCapacity *= 2;

        System.Array.Resize(ref TargetPositions, newCapacity);
        System.Array.Resize(ref DamageMultipliers, newCapacity);
    }
}
