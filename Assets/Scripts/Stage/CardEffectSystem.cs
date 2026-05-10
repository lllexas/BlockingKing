using UnityEngine;

/// <summary>
/// 卡牌效果执行器。
/// 早期直接按 CardID switch 分发，后续再拆细分 effect module。
/// </summary>
public class CardEffectSystem : MonoBehaviour
{
    public static CardEffectSystem Instance { get; private set; }

    [SerializeField, Min(1)] private int materializedTerrainWallHealth = 3;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ConfigureMaterializedTerrainWallHealth(int health)
    {
        materializedTerrainWallHealth = Mathf.Max(1, health);
    }

    public bool Execute(EntityHandle actor, CardIntent intent)
    {
        if (intent == null || !intent.Active)
            return false;

        var flow = GameFlowController.Instance;
        if (flow == null || !flow.IsInLevel)
            return true;

        var card = intent.Card;
        if (card == null)
        {
            Debug.LogWarning("[CardEffectSystem] CardIntent is missing Card instance.");
            return false;
        }

        string cardId = ResolveCardId(card);
        if (string.IsNullOrWhiteSpace(cardId))
        {
            Debug.LogWarning("[CardEffectSystem] Card instance is missing cardId.");
            return false;
        }

        switch (cardId)
        {
            case "rook.charge":
                return ExecuteLineCharge(actor, intent.Direction, DirectionMask.Orthogonal);
            case "bishop.charge":
                return ExecuteLineCharge(actor, intent.Direction, DirectionMask.Diagonal);
            case "queen.charge":
                return ExecuteLineCharge(actor, intent.Direction, DirectionMask.EightWay);
            case "soldier.charge":
                return ExecuteSoldierCharge(actor, intent.Direction);
            case "knight.stomp":
                return ExecuteKnightStomp(actor, intent.TargetCell);
            case "cannon.charge":
                return ExecuteCannonCharge(actor, intent.Direction);
            case "king.stomp":
                return ExecuteKingStomp(actor);
            default:
                Debug.Log($"[CardEffectSystem] Unhandled card '{cardId}' direction={intent.Direction} instance='{card.instanceId ?? string.Empty}'.");
                break;
        }

        return true;
    }

    private enum DirectionMask
    {
        Orthogonal,
        Diagonal,
        EightWay
    }

    private bool ExecuteLineCharge(EntityHandle actor, Vector2Int direction, DirectionMask mask)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor))
            return false;

        if (!IsDirectionAllowed(direction, mask))
            return false;

        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        bool acted = false;
        while (entitySystem.IsValid(actor))
        {
            actorIndex = entitySystem.GetIndex(actor);
            if (actorIndex < 0)
                break;

            Vector2Int current = entitySystem.entities.coreComponents[actorIndex].Position;
            Vector2Int next = current + direction;
            if (!entitySystem.IsInsideMap(next))
                break;

            if (entitySystem.IsWall(next))
            {
                AttackTerrainWall(entitySystem, actor, next);
                acted = true;
                break;
            }

            EntityHandle occupant = entitySystem.GetOccupant(next);
            if (!entitySystem.IsValid(occupant))
            {
                MoveEntity(entitySystem, actor, next);
                acted = true;
                continue;
            }

            int occupantIndex = entitySystem.GetIndex(occupant);
            if (occupantIndex < 0)
                break;

            EntityType occupantType = entitySystem.entities.coreComponents[occupantIndex].EntityType;
            if (occupantType == EntityType.Enemy)
            {
                bool killed = DamageEntity(entitySystem, actor, occupant);
                acted = true;
                if (!killed)
                    break;

                MoveEntity(entitySystem, actor, next);
                continue;
            }

            if (occupantType == EntityType.Box)
            {
                if (!TryPushBoxOneStep(entitySystem, occupant, direction))
                    break;

                MoveEntity(entitySystem, actor, next);
                acted = true;
                continue;
            }

            if (occupantType == EntityType.Wall)
            {
                DamageEntity(entitySystem, actor, occupant);
                acted = true;
            }

            break;
        }

        return acted;
    }

    private bool ExecuteSoldierCharge(EntityHandle actor, Vector2Int direction)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor))
            return false;

        if (!IsDirectionAllowed(direction, DirectionMask.Diagonal))
            return false;

        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        Vector2Int current = entitySystem.entities.coreComponents[actorIndex].Position;
        Vector2Int next = current + direction;
        if (!entitySystem.IsInsideMap(next))
            return false;

        if (entitySystem.IsWall(next))
            return AttackTerrainWall(entitySystem, actor, next);

        EntityHandle occupant = entitySystem.GetOccupant(next);
        if (!entitySystem.IsValid(occupant))
        {
            MoveEntity(entitySystem, actor, next);
            return true;
        }

        int occupantIndex = entitySystem.GetIndex(occupant);
        if (occupantIndex < 0)
            return false;

        EntityType occupantType = entitySystem.entities.coreComponents[occupantIndex].EntityType;
        if (occupantType == EntityType.Wall)
        {
            DamageEntity(entitySystem, actor, occupant);
            return true;
        }

        if (occupantType != EntityType.Enemy)
            return false;

        if (!DamageEntity(entitySystem, actor, occupant))
            return true;

        MoveEntity(entitySystem, actor, next);
        return true;
    }

    private bool ExecuteKingStomp(EntityHandle actor)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor))
            return false;

        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        bool acted = false;
        Vector2Int center = entitySystem.entities.coreComponents[actorIndex].Position;
        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                Vector2Int pos = center + new Vector2Int(x, y);
                if (!entitySystem.IsInsideMap(pos))
                    continue;

                if (entitySystem.IsWall(pos))
                {
                    AttackTerrainWall(entitySystem, actor, pos);
                    acted = true;
                    continue;
                }

                EntityHandle target = entitySystem.GetOccupant(pos);
                if (!entitySystem.IsValid(target))
                    continue;

                int targetIndex = entitySystem.GetIndex(target);
                if (targetIndex < 0)
                    continue;

                EntityType targetType = entitySystem.entities.coreComponents[targetIndex].EntityType;
                if (targetType != EntityType.Enemy && targetType != EntityType.Wall)
                    continue;

                DamageEntity(entitySystem, actor, target);
                acted = true;
            }
        }

        return acted;
    }

    private bool ExecuteKnightStomp(EntityHandle actor, Vector2Int targetCell)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor))
            return false;

        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        Vector2Int current = entitySystem.entities.coreComponents[actorIndex].Position;
        Vector2Int delta = targetCell - current;
        int absX = Mathf.Abs(delta.x);
        int absY = Mathf.Abs(delta.y);
        if (!((absX == 1 && absY == 2) || (absX == 2 && absY == 1)))
            return false;

        if (!entitySystem.IsInsideMap(targetCell))
            return false;

        if (entitySystem.IsWall(targetCell))
            return AttackTerrainWall(entitySystem, actor, targetCell);

        EntityHandle occupant = entitySystem.GetOccupant(targetCell);
        if (!entitySystem.IsValid(occupant))
        {
            MoveEntity(entitySystem, actor, targetCell);
            return true;
        }

        int occupantIndex = entitySystem.GetIndex(occupant);
        if (occupantIndex < 0)
            return false;

        EntityType occupantType = entitySystem.entities.coreComponents[occupantIndex].EntityType;
        if (occupantType == EntityType.Wall)
        {
            DamageEntity(entitySystem, actor, occupant);
            return true;
        }

        if (occupantType != EntityType.Enemy)
            return false;

        if (!DamageEntity(entitySystem, actor, occupant))
            return true;

        MoveEntity(entitySystem, actor, targetCell);
        return true;
    }

    private bool ExecuteCannonCharge(EntityHandle actor, Vector2Int direction)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor))
            return false;

        if (!IsDirectionAllowed(direction, DirectionMask.Orthogonal))
            return false;

        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        bool hasJumped = false;
        bool acted = false;
        while (entitySystem.IsValid(actor))
        {
            actorIndex = entitySystem.GetIndex(actor);
            if (actorIndex < 0)
                break;

            Vector2Int current = entitySystem.entities.coreComponents[actorIndex].Position;
            Vector2Int next = current + direction;
            if (!entitySystem.IsInsideMap(next))
                break;

            if (entitySystem.IsWall(next))
            {
                AttackTerrainWall(entitySystem, actor, next);
                acted = true;
                break;
            }

            EntityHandle occupant = entitySystem.GetOccupant(next);
            if (!entitySystem.IsValid(occupant))
            {
                MoveEntity(entitySystem, actor, next);
                acted = true;
                continue;
            }

            int occupantIndex = entitySystem.GetIndex(occupant);
            if (occupantIndex < 0)
                break;

            EntityType occupantType = entitySystem.entities.coreComponents[occupantIndex].EntityType;
            if (!hasJumped && (occupantType == EntityType.Enemy || occupantType == EntityType.Box))
            {
                Vector2Int landing = next + direction;
                if (!entitySystem.IsInsideMap(landing) || entitySystem.IsWall(landing))
                    break;

                if (entitySystem.IsValid(entitySystem.GetOccupant(landing)))
                    break;

                MoveEntity(entitySystem, actor, landing);
                hasJumped = true;
                acted = true;
                continue;
            }

            if (occupantType == EntityType.Enemy)
            {
                bool killed = DamageEntity(entitySystem, actor, occupant);
                acted = true;
                if (killed)
                    MoveEntity(entitySystem, actor, next);

                break;
            }

            if (occupantType == EntityType.Box)
            {
                if (TryPushBoxOneStep(entitySystem, occupant, direction))
                {
                    MoveEntity(entitySystem, actor, next);
                    acted = true;
                }

                break;
            }

            if (occupantType == EntityType.Wall)
            {
                DamageEntity(entitySystem, actor, occupant);
                acted = true;
                break;
            }

            break;
        }

        return acted;
    }

    private static bool TryPushBoxOneStep(EntitySystem entitySystem, EntityHandle box, Vector2Int direction)
    {
        int boxIndex = entitySystem.GetIndex(box);
        if (boxIndex < 0)
            return false;

        Vector2Int boxPosition = entitySystem.entities.coreComponents[boxIndex].Position;
        Vector2Int next = boxPosition + direction;
        if (!entitySystem.IsInsideMap(next) || entitySystem.IsWall(next))
            return false;

        if (entitySystem.IsValid(entitySystem.GetOccupant(next)))
            return false;

        MoveEntity(entitySystem, box, next);
        return true;
    }

    private static bool DamageEntity(EntitySystem entitySystem, EntityHandle actor, EntityHandle target)
    {
        int actorIndex = entitySystem.GetIndex(actor);
        int targetIndex = entitySystem.GetIndex(target);
        if (actorIndex < 0 || targetIndex < 0)
            return false;

        int damage = Mathf.Max(1, CombatStats.GetAttack(entitySystem.entities.statusComponents[actorIndex]));
        ref var targetCore = ref entitySystem.entities.coreComponents[targetIndex];
        ref var targetStatus = ref entitySystem.entities.statusComponents[targetIndex];
        CombatStats.DealDamage(ref targetStatus, damage);
        int currentHealth = CombatStats.GetCurrentHealth(targetStatus);
        Debug.Log($"[CardEffectSystem] Hit {targetCore.EntityType} at {targetCore.Position}, damage={damage}, health={currentHealth}");

        return currentHealth <= 0;
    }

    private bool AttackTerrainWall(EntitySystem entitySystem, EntityHandle actor, Vector2Int position)
    {
        bool hadWallEntity = entitySystem.IsValid(entitySystem.GetOccupant(position));
        EntityHandle wall = entitySystem.TryMaterializeWall(position, materializedTerrainWallHealth);
        if (!entitySystem.IsValid(wall))
            return false;

        if (!hadWallEntity)
            Debug.Log($"[CardEffectSystem] Materialized terrain wall at {position}, health={materializedTerrainWallHealth}");

        DamageEntity(entitySystem, actor, wall);
        return true;
    }

    private static void MoveEntity(EntitySystem entitySystem, EntityHandle actor, Vector2Int next)
    {
        int index = entitySystem.GetIndex(actor);
        if (index < 0)
            return;

        var entities = entitySystem.entities;
        Vector2Int current = entities.coreComponents[index].Position;
        int currentMapIndex = ToMapIndex(entities, current);
        int nextMapIndex = ToMapIndex(entities, next);

        if (entities.coreComponents[index].OccupiesGrid && entities.gridMap[currentMapIndex] == actor.Id)
            entities.gridMap[currentMapIndex] = -1;

        if (entities.coreComponents[index].OccupiesGrid)
            entities.gridMap[nextMapIndex] = actor.Id;

        entities.coreComponents[index].Position = next;

        EventBusSystem.Instance?.Publish(new StageEvent(
            StageEventType.EntityMoved,
            actor: actor,
            entity: actor,
            entityType: entities.coreComponents[index].EntityType,
            from: current,
            to: next,
            sourceTagId: entities.propertyComponents[index].SourceTagId));
    }

    private static bool IsDirectionAllowed(Vector2Int direction, DirectionMask mask)
    {
        if (direction == Vector2Int.zero)
            return false;

        int absX = Mathf.Abs(direction.x);
        int absY = Mathf.Abs(direction.y);
        if (absX > 1 || absY > 1)
            return false;

        bool orthogonal = absX + absY == 1;
        bool diagonal = absX == 1 && absY == 1;

        return mask switch
        {
            DirectionMask.Orthogonal => orthogonal,
            DirectionMask.Diagonal => diagonal,
            DirectionMask.EightWay => orthogonal || diagonal,
            _ => false
        };
    }

    private static int ToMapIndex(EntityComponents entities, Vector2Int pos)
    {
        return pos.y * entities.mapWidth + pos.x;
    }

    private static string ResolveCardId(CardSO card)
    {
        if (card == null)
            return null;

        return !string.IsNullOrWhiteSpace(card.cardId) ? card.cardId : card.name;
    }
}
