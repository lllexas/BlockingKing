using UnityEngine;

/// <summary>
/// 攻击执行 + 攻击相关特效/音效逻辑。
/// </summary>
public class AttackSystem : MonoBehaviour
{
    public static AttackSystem Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Execute(EntityHandle actor, AttackIntent intent)
    {
        if (intent == null)
            return;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor))
            return;

        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return;

        int attack = entitySystem.entities.propertyComponents[actorIndex].Attack;
        if (attack <= 0)
            return;

        for (int i = 0; i < intent.TargetCount; i++)
        {
            Vector2Int targetPosition = intent.TargetPositions[i];
            float multiplier = intent.DamageMultipliers[i];
            int damage = Mathf.Max(1, Mathf.RoundToInt(attack * multiplier));
            ApplyDamageToCell(entitySystem, targetPosition, damage);
        }
    }

    private static void ApplyDamageToCell(EntitySystem entitySystem, Vector2Int targetPosition, int damage)
    {
        if (damage <= 0 || !entitySystem.IsInsideMap(targetPosition))
            return;

        EntityHandle target = entitySystem.GetOccupant(targetPosition);
        if (!entitySystem.IsValid(target))
            return;

        int targetIndex = entitySystem.GetIndex(target);
        if (targetIndex < 0)
            return;

        ref var core = ref entitySystem.entities.coreComponents[targetIndex];
        if (core.EntityType != EntityType.Enemy && core.EntityType != EntityType.Wall)
            return;

        core.Health -= damage;
        Debug.Log($"[AttackSystem] Hit {core.EntityType} at {targetPosition}, damage={damage}, health={core.Health}");

        if (core.Health <= 0)
            entitySystem.DestroyEntity(target);
    }
}
