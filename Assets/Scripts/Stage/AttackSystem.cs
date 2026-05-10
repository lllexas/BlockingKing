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

        int attack = CombatStats.GetAttack(entitySystem.entities.statusComponents[actorIndex]);
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
        if (!CanReceiveAttackDamage(entitySystem, targetIndex))
            return;

        ref var status = ref entitySystem.entities.statusComponents[targetIndex];
        CombatStats.DealDamage(ref status, damage);
        Debug.Log($"[AttackSystem] Hit {core.EntityType} at {targetPosition}, damage={damage}, health={CombatStats.GetCurrentHealth(status)}");
    }

    private static bool CanReceiveAttackDamage(EntitySystem entitySystem, int targetIndex)
    {
        var entities = entitySystem.entities;
        EntityType entityType = entities.coreComponents[targetIndex].EntityType;
        if (entityType == EntityType.Target)
            return false;

        if (entityType == EntityType.Box && !entities.propertyComponents[targetIndex].IsCore)
            return false;

        return true;
    }
}
