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

        Vector2Int actorPosition = entitySystem.entities.coreComponents[actorIndex].Position;
        int attack = CombatStats.GetAttack(entitySystem.entities.statusComponents[actorIndex]);
        if (attack <= 0)
            return;

        for (int i = 0; i < intent.TargetCount; i++)
        {
            Vector2Int targetPosition = intent.TargetPositions[i];
            float multiplier = intent.DamageMultipliers[i];
            int damage = Mathf.Max(1, Mathf.RoundToInt(attack * multiplier));
            ApplyDamageToCell(entitySystem, actor, actorPosition, targetPosition, damage);
        }
    }

    private static void ApplyDamageToCell(EntitySystem entitySystem, EntityHandle actor, Vector2Int actorPosition, Vector2Int targetPosition, int damage)
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
        int previousBlock = status.Block;
        CombatStats.DealDamage(ref status, damage);
        if (core.EntityType == EntityType.Box && entitySystem.entities.propertyComponents[targetIndex].IsCore)
            SyncCoreBoxHealthToPlayer(entitySystem, targetIndex);
        else if (core.EntityType == EntityType.Player)
            SyncPlayerHealthToCoreBoxes(entitySystem, targetIndex);

        EventBusSystem.Instance?.Publish(new StageEvent(
            StageEventType.EntityDamaged,
            actor: actor,
            entity: target,
            entityType: core.EntityType,
            from: targetPosition,
            to: targetPosition,
            sourceTagId: entitySystem.entities.propertyComponents[targetIndex].SourceTagId,
            currentHealth: CombatStats.GetCurrentHealth(status),
            sourcePosition: actorPosition,
            hasSourcePosition: actorPosition != targetPosition));

        int absorbed = Mathf.Max(0, previousBlock - status.Block);
        Debug.Log($"[AttackSystem] Hit {core.EntityType} at {targetPosition}, damage={damage}, blockAbsorbed={absorbed}, health={CombatStats.GetCurrentHealth(status)}, block={status.Block}");
    }

    private static void SyncCoreBoxHealthToPlayer(EntitySystem entitySystem, int coreBoxIndex)
    {
        var entities = entitySystem.entities;
        ref var coreBoxStatus = ref entities.statusComponents[coreBoxIndex];
        int currentHealth = CombatStats.GetCurrentHealth(coreBoxStatus);
        int maxHealth = CombatStats.GetMaxHealth(coreBoxStatus);

        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            ref var playerStatus = ref entities.statusComponents[i];
            playerStatus.BaseMaxHealth = maxHealth;
            playerStatus.MaxHealthModifier = 0;
            playerStatus.DamageTaken = Mathf.Max(0, maxHealth - currentHealth);
            return;
        }
    }

    private static void SyncPlayerHealthToCoreBoxes(EntitySystem entitySystem, int playerIndex)
    {
        var entities = entitySystem.entities;
        ref var playerStatus = ref entities.statusComponents[playerIndex];
        int currentHealth = CombatStats.GetCurrentHealth(playerStatus);
        int maxHealth = CombatStats.GetMaxHealth(playerStatus);

        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Box ||
                !entities.propertyComponents[i].IsCore)
            {
                continue;
            }

            ref var coreBoxStatus = ref entities.statusComponents[i];
            coreBoxStatus.BaseMaxHealth = maxHealth;
            coreBoxStatus.MaxHealthModifier = 0;
            coreBoxStatus.DamageTaken = Mathf.Max(0, maxHealth - currentHealth);
        }
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
