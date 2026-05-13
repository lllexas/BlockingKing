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

    public bool TryPreviewDamageCell(EntityHandle actor, CardSO card, CardReleaseTarget target, Vector2Int watchedCell)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor) || card == null)
            return false;

        string cardId = ResolveCardId(card);
        return cardId switch
        {
            "rook.charge" => PreviewLineCharge(actor, target.Direction, DirectionMask.Orthogonal, watchedCell),
            "bishop.charge" => PreviewLineCharge(actor, target.Direction, DirectionMask.Diagonal, watchedCell),
            "queen.charge" => PreviewLineCharge(actor, target.Direction, DirectionMask.EightWay, watchedCell),
            "soldier.charge" => PreviewSoldierCharge(actor, target.Direction, watchedCell),
            "knight.stomp" => PreviewTargetCellAttack(target.TargetCell, watchedCell),
            "cannon.charge" => PreviewCannonCharge(actor, target.Direction, watchedCell),
            "king.stomp" => PreviewKingStomp(actor, watchedCell),
            _ => false
        };
    }

    private enum DirectionMask
    {
        Orthogonal,
        Diagonal,
        EightWay
    }

    private bool PreviewLineCharge(EntityHandle actor, Vector2Int direction, DirectionMask mask, Vector2Int watchedCell)
    {
        var entitySystem = EntitySystem.Instance;
        if (!IsDirectionAllowed(direction, mask))
            return false;

        if (!TryGetActorPosition(entitySystem, actor, out var current))
            return false;

        int attack = GetActorAttack(entitySystem, actor);
        while (true)
        {
            Vector2Int next = current + direction;
            if (!entitySystem.IsInsideMap(next))
                return false;

            if (entitySystem.IsWall(next))
                return next == watchedCell;

            EntityHandle occupant = entitySystem.GetOccupant(next);
            if (!entitySystem.IsValid(occupant))
            {
                current = next;
                continue;
            }

            int occupantIndex = entitySystem.GetIndex(occupant);
            if (occupantIndex < 0)
                return false;

            EntityType occupantType = entitySystem.entities.coreComponents[occupantIndex].EntityType;
            if (occupantType == EntityType.Enemy || occupantType == EntityType.Wall)
                return next == watchedCell || (WouldKill(entitySystem, occupantIndex, attack) && PreviewLineChargeFrom(next, direction, watchedCell, attack, mask));

            if (occupantType == EntityType.Box && BoxDisplacementUtility.CanPreviewPushOrBounce(entitySystem, next, direction))
            {
                current = next;
                continue;
            }

            return false;
        }
    }

    private bool PreviewLineChargeFrom(Vector2Int current, Vector2Int direction, Vector2Int watchedCell, int attack, DirectionMask mask)
    {
        var entitySystem = EntitySystem.Instance;
        while (true)
        {
            Vector2Int next = current + direction;
            if (!entitySystem.IsInsideMap(next))
                return false;

            if (entitySystem.IsWall(next))
                return next == watchedCell;

            EntityHandle occupant = entitySystem.GetOccupant(next);
            if (!entitySystem.IsValid(occupant))
            {
                current = next;
                continue;
            }

            int occupantIndex = entitySystem.GetIndex(occupant);
            if (occupantIndex < 0)
                return false;

            EntityType occupantType = entitySystem.entities.coreComponents[occupantIndex].EntityType;
            if (occupantType == EntityType.Enemy || occupantType == EntityType.Wall)
                return next == watchedCell || (WouldKill(entitySystem, occupantIndex, attack) && PreviewLineChargeFrom(next, direction, watchedCell, attack, mask));

            if (occupantType == EntityType.Box && BoxDisplacementUtility.CanPreviewPushOrBounce(entitySystem, next, direction))
            {
                current = next;
                continue;
            }

            return false;
        }
    }

    private bool PreviewStepAttack(EntityHandle actor, Vector2Int direction, DirectionMask mask, Vector2Int watchedCell)
    {
        var entitySystem = EntitySystem.Instance;
        if (!IsDirectionAllowed(direction, mask) || !TryGetActorPosition(entitySystem, actor, out var current))
            return false;

        Vector2Int next = current + direction;
        if (!entitySystem.IsInsideMap(next))
            return false;

        if (entitySystem.IsWall(next))
            return next == watchedCell;

        return IsDamageableOccupantAt(entitySystem, next) && next == watchedCell;
    }

    private bool PreviewSoldierCharge(EntityHandle actor, Vector2Int direction, Vector2Int watchedCell)
    {
        var entitySystem = EntitySystem.Instance;
        if (!IsDirectionAllowed(direction, DirectionMask.Diagonal) || !TryGetActorPosition(entitySystem, actor, out var current))
            return false;

        Vector2Int next = current + direction;
        if (!entitySystem.IsInsideMap(next))
            return false;

        if (entitySystem.IsWall(next))
            return next == watchedCell;

        EntityHandle occupant = entitySystem.GetOccupant(next);
        if (!entitySystem.IsValid(occupant))
            return false;

        int occupantIndex = entitySystem.GetIndex(occupant);
        if (occupantIndex < 0)
            return false;

        EntityType occupantType = entitySystem.entities.coreComponents[occupantIndex].EntityType;
        if (occupantType == EntityType.Enemy || occupantType == EntityType.Wall)
            return next == watchedCell;

        if (occupantType == EntityType.Box && BoxDisplacementUtility.CanPreviewPushOrBounce(entitySystem, next, direction))
            return next == watchedCell;

        return false;
    }

    private bool PreviewTargetCellAttack(Vector2Int targetCell, Vector2Int watchedCell)
    {
        var entitySystem = EntitySystem.Instance;
        if (!entitySystem.IsInsideMap(targetCell))
            return false;

        return targetCell == watchedCell && (entitySystem.IsWall(targetCell) || IsDamageableOccupantAt(entitySystem, targetCell));
    }

    private bool PreviewKingStomp(EntityHandle actor, Vector2Int watchedCell)
    {
        var entitySystem = EntitySystem.Instance;
        if (!TryGetActorPosition(entitySystem, actor, out var center))
            return false;

        Vector2Int delta = watchedCell - center;
        if (delta == Vector2Int.zero || Mathf.Abs(delta.x) > 1 || Mathf.Abs(delta.y) > 1)
            return false;

        return entitySystem.IsInsideMap(watchedCell) && (entitySystem.IsWall(watchedCell) || IsDamageableOccupantAt(entitySystem, watchedCell));
    }

    private bool PreviewCannonCharge(EntityHandle actor, Vector2Int direction, Vector2Int watchedCell)
    {
        var entitySystem = EntitySystem.Instance;
        if (!IsDirectionAllowed(direction, DirectionMask.Orthogonal) || !TryGetActorPosition(entitySystem, actor, out var current))
            return false;

        bool hasJumped = false;
        while (true)
        {
            Vector2Int next = current + direction;
            if (!entitySystem.IsInsideMap(next))
                return false;

            if (entitySystem.IsWall(next))
                return next == watchedCell;

            EntityHandle occupant = entitySystem.GetOccupant(next);
            if (!entitySystem.IsValid(occupant))
            {
                current = next;
                continue;
            }

            int occupantIndex = entitySystem.GetIndex(occupant);
            if (occupantIndex < 0)
                return false;

            EntityType occupantType = entitySystem.entities.coreComponents[occupantIndex].EntityType;
            if (!hasJumped && (occupantType == EntityType.Enemy || occupantType == EntityType.Box))
            {
                Vector2Int landing = next + direction;
                if (!entitySystem.IsInsideMap(landing) || entitySystem.IsWall(landing) || entitySystem.IsValid(entitySystem.GetOccupant(landing)))
                    return false;

                current = landing;
                hasJumped = true;
                continue;
            }

            if (occupantType == EntityType.Enemy || occupantType == EntityType.Wall)
                return next == watchedCell;

            return false;
        }
    }

    private static bool TryGetActorPosition(EntitySystem entitySystem, EntityHandle actor, out Vector2Int position)
    {
        position = default;
        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        position = entitySystem.entities.coreComponents[actorIndex].Position;
        return true;
    }

    private static int GetActorAttack(EntitySystem entitySystem, EntityHandle actor)
    {
        int actorIndex = entitySystem.GetIndex(actor);
        return actorIndex >= 0 ? Mathf.Max(1, CombatStats.GetAttack(entitySystem.entities.statusComponents[actorIndex])) : 1;
    }

    private static bool WouldKill(EntitySystem entitySystem, int targetIndex, int damage)
    {
        return CombatStats.GetCurrentHealth(entitySystem.entities.statusComponents[targetIndex]) - damage <= 0;
    }

    private static bool IsDamageableOccupantAt(EntitySystem entitySystem, Vector2Int cell)
    {
        EntityHandle occupant = entitySystem.GetOccupant(cell);
        if (!entitySystem.IsValid(occupant))
            return false;

        int index = entitySystem.GetIndex(occupant);
        if (index < 0)
            return false;

        EntityType type = entitySystem.entities.coreComponents[index].EntityType;
        return type == EntityType.Enemy || type == EntityType.Wall;
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
                bool killed = AttackTerrainWall(entitySystem, actor, next, out _);
                if (killed)
                    MoveEntity(entitySystem, actor, next);
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
                if (!BoxDisplacementUtility.TryPushOrBounce(entitySystem, occupant, direction))
                    break;

                MoveEntity(entitySystem, actor, next);
                acted = true;
                continue;
            }

            if (occupantType == EntityType.Wall)
            {
                bool killed = DamageEntity(entitySystem, actor, occupant);
                if (killed)
                    MoveEntity(entitySystem, actor, next);
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
        {
            bool killed = AttackTerrainWall(entitySystem, actor, next, out bool attacked);
            if (killed)
                MoveEntity(entitySystem, actor, next);

            return attacked;
        }

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
            bool killed = DamageEntity(entitySystem, actor, occupant);
            if (killed)
                MoveEntity(entitySystem, actor, next);
            return true;
        }

        if (occupantType == EntityType.Box)
        {
            if (!BoxDisplacementUtility.TryPushOrBounce(entitySystem, occupant, direction))
                return false;

            MoveEntity(entitySystem, actor, next);
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
                    AttackTerrainWall(entitySystem, actor, pos, out _);
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
            return AttackTerrainWall(entitySystem, actor, targetCell, out _);

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
                bool killed = AttackTerrainWall(entitySystem, actor, next, out _);
                if (killed)
                    MoveEntity(entitySystem, actor, next);
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
                if (BoxDisplacementUtility.TryPushOrBounce(entitySystem, occupant, direction))
                {
                    MoveEntity(entitySystem, actor, next);
                    acted = true;
                }

                break;
            }

            if (occupantType == EntityType.Wall)
            {
                bool killed = DamageEntity(entitySystem, actor, occupant);
                if (killed)
                    MoveEntity(entitySystem, actor, next);
                acted = true;
                break;
            }

            break;
        }

        return acted;
    }

    private static bool DamageEntity(EntitySystem entitySystem, EntityHandle actor, EntityHandle target)
    {
        int actorIndex = entitySystem.GetIndex(actor);
        int targetIndex = entitySystem.GetIndex(target);
        if (actorIndex < 0 || targetIndex < 0)
            return false;

        int damage = Mathf.Max(1, CombatStats.GetAttack(entitySystem.entities.statusComponents[actorIndex]));
        Vector2Int actorPosition = entitySystem.entities.coreComponents[actorIndex].Position;
        ref var targetCore = ref entitySystem.entities.coreComponents[targetIndex];
        ref var targetStatus = ref entitySystem.entities.statusComponents[targetIndex];
        CombatStats.DealDamage(ref targetStatus, damage);
        int currentHealth = CombatStats.GetCurrentHealth(targetStatus);
        EventBusSystem.Instance?.Publish(new StageEvent(
            StageEventType.EntityDamaged,
            actor: actor,
            entity: target,
            entityType: targetCore.EntityType,
            from: targetCore.Position,
            to: targetCore.Position,
            sourceTagId: entitySystem.entities.propertyComponents[targetIndex].SourceTagId,
            currentHealth: currentHealth,
            sourcePosition: actorPosition,
            hasSourcePosition: actorPosition != targetCore.Position));
        Debug.Log($"[CardEffectSystem] Hit {targetCore.EntityType} at {targetCore.Position}, damage={damage}, health={currentHealth}");

        return currentHealth <= 0;
    }

    private bool AttackTerrainWall(EntitySystem entitySystem, EntityHandle actor, Vector2Int position, out bool attacked)
    {
        attacked = false;
        bool hadWallEntity = entitySystem.IsValid(entitySystem.GetOccupant(position));
        EntityHandle wall = entitySystem.TryMaterializeWall(position, materializedTerrainWallHealth);
        if (!entitySystem.IsValid(wall))
            return false;

        if (!hadWallEntity)
            Debug.Log($"[CardEffectSystem] Materialized terrain wall at {position}, health={materializedTerrainWallHealth}");

        attacked = true;
        return DamageEntity(entitySystem, actor, wall);
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
