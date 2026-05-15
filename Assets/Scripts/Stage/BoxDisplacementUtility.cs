using UnityEngine;

public readonly struct BoxPushContext
{
    public readonly EntityHandle Pusher;
    public readonly int Damage;
    public readonly bool AllowOverrun;
    public readonly bool AllowBounce;
    public readonly Vector2Int SourcePosition;
    public readonly bool HasSourcePosition;

    public BoxPushContext(EntityHandle pusher, int damage, bool allowOverrun, Vector2Int sourcePosition = default, bool hasSourcePosition = false, bool allowBounce = true)
    {
        Pusher = pusher;
        Damage = Mathf.Max(0, damage);
        AllowOverrun = allowOverrun;
        AllowBounce = allowBounce;
        SourcePosition = sourcePosition;
        HasSourcePosition = hasSourcePosition;
    }
}

public static class BoxDisplacementUtility
{
    public static bool CanPreviewPushOrBounce(EntitySystem entitySystem, Vector2Int boxCell, Vector2Int direction)
    {
        return CanPreviewPushOrBounce(entitySystem, boxCell, direction, new BoxPushContext(EntityHandle.None, 0, false));
    }

    public static bool CanPreviewPushOrBounce(EntitySystem entitySystem, Vector2Int boxCell, Vector2Int direction, BoxPushContext context)
    {
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsInsideMap(boxCell))
            return false;

        EntityHandle box = entitySystem.GetOccupant(boxCell);
        if (!entitySystem.IsValid(box) || IsLockedCoreBox(entitySystem, box))
            return false;

        return TryGetPushDestination(entitySystem, boxCell, direction, out _) ||
               CanCrushOverrunTarget(entitySystem, boxCell, direction, context) ||
               (context.AllowBounce && TryGetEdgeBounceDestination(entitySystem, boxCell, out _));
    }

    public static bool TryPushOrBounce(EntitySystem entitySystem, EntityHandle box, Vector2Int direction)
    {
        return TryPushOrBounce(entitySystem, box, direction, new BoxPushContext(EntityHandle.None, 0, false));
    }

    public static bool TryPushOrBounce(EntitySystem entitySystem, EntityHandle box, Vector2Int direction, BoxPushContext context)
    {
        if (!TryGetBoxPosition(entitySystem, box, out var boxPosition))
            return false;

        if (TryGetPushDestination(entitySystem, boxPosition, direction, out var destination) ||
            TryCrushOverrunTarget(entitySystem, boxPosition, direction, context, out destination) ||
            (context.AllowBounce && TryGetEdgeBounceDestination(entitySystem, boxPosition, out destination)))
        {
            MoveBox(entitySystem, box, destination);
            return true;
        }

        return false;
    }

    public static bool TryBounceEdgeBox(EntitySystem entitySystem, EntityHandle box)
    {
        if (!TryGetBoxPosition(entitySystem, box, out var boxPosition))
            return false;

        if (!TryGetEdgeBounceDestination(entitySystem, boxPosition, out var destination))
            return false;

        MoveBox(entitySystem, box, destination);
        return true;
    }

    public static bool CanBounceEdgeBox(EntitySystem entitySystem, EntityHandle box)
    {
        if (!TryGetBoxPosition(entitySystem, box, out var boxPosition))
            return false;

        return TryGetEdgeBounceDestination(entitySystem, boxPosition, out _);
    }

    private static bool TryGetBoxPosition(EntitySystem entitySystem, EntityHandle box, out Vector2Int boxPosition)
    {
        boxPosition = default;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(box) || IsLockedCoreBox(entitySystem, box))
            return false;

        int boxIndex = entitySystem.GetIndex(box);
        if (boxIndex < 0 || entitySystem.entities.coreComponents[boxIndex].EntityType != EntityType.Box)
            return false;

        boxPosition = entitySystem.entities.coreComponents[boxIndex].Position;
        return true;
    }

    private static bool TryGetPushDestination(EntitySystem entitySystem, Vector2Int boxPosition, Vector2Int direction, out Vector2Int destination)
    {
        destination = boxPosition + direction;
        return direction != Vector2Int.zero &&
               entitySystem.IsInsideMap(destination) &&
               !entitySystem.IsWall(destination) &&
               !entitySystem.IsValid(entitySystem.GetOccupant(destination));
    }

    private static bool CanCrushOverrunTarget(EntitySystem entitySystem, Vector2Int boxPosition, Vector2Int direction, BoxPushContext context)
    {
        if (!TryGetOverrunTarget(entitySystem, boxPosition, direction, context, out _, out int targetIndex))
            return false;

        int currentHealth = CombatStats.GetCurrentHealth(entitySystem.entities.statusComponents[targetIndex]);
        return context.Damage >= currentHealth;
    }

    private static bool TryCrushOverrunTarget(EntitySystem entitySystem, Vector2Int boxPosition, Vector2Int direction, BoxPushContext context, out Vector2Int destination)
    {
        destination = default;
        if (!TryGetOverrunTarget(entitySystem, boxPosition, direction, context, out var target, out int targetIndex))
            return false;

        ref var targetStatus = ref entitySystem.entities.statusComponents[targetIndex];
        int previousHealth = CombatStats.GetCurrentHealth(targetStatus);
        if (context.Damage < previousHealth)
            return false;

        var statusEffects = StatusEffectSystem.Instance;
        if (statusEffects == null || !statusEffects.TryConsumeStacks(context.Pusher, StatusEffectSystem.OverrunEffectId))
            return false;

        ref var targetCore = ref entitySystem.entities.coreComponents[targetIndex];
        Vector2Int targetPosition = targetCore.Position;
        int sourceTagId = entitySystem.entities.propertyComponents[targetIndex].SourceTagId;
        Vector2Int sourcePosition = context.HasSourcePosition ? context.SourcePosition : boxPosition;

        targetStatus.DamageTaken = Mathf.Max(0, targetStatus.DamageTaken) + context.Damage;
        int currentHealth = CombatStats.GetCurrentHealth(targetStatus);
        EventBusSystem.Instance?.Publish(new StageEvent(
            StageEventType.EntityDamaged,
            actor: context.Pusher,
            entity: target,
            entityType: targetCore.EntityType,
            from: targetPosition,
            to: targetPosition,
            sourceTagId: sourceTagId,
            currentHealth: currentHealth,
            sourcePosition: sourcePosition,
            hasSourcePosition: sourcePosition != targetPosition));

        entitySystem.DestroyEntity(target, currentHealth, sourcePosition, sourcePosition != targetPosition);
        destination = targetPosition;
        Debug.Log($"[BoxDisplacementUtility] Overrun crushed enemy at {targetPosition}, damage={context.Damage}, previousHealth={previousHealth}, remainingStacks={statusEffects.GetStacks(context.Pusher, StatusEffectSystem.OverrunEffectId)}");
        return true;
    }

    private static bool TryGetOverrunTarget(EntitySystem entitySystem, Vector2Int boxPosition, Vector2Int direction, BoxPushContext context, out EntityHandle target, out int targetIndex)
    {
        target = EntityHandle.None;
        targetIndex = -1;
        if (!context.AllowOverrun || context.Damage <= 0 || direction == Vector2Int.zero)
            return false;

        var statusEffects = StatusEffectSystem.Instance;
        if (statusEffects == null || !statusEffects.HasStacks(context.Pusher, StatusEffectSystem.OverrunEffectId))
            return false;

        Vector2Int targetCell = boxPosition + direction;
        if (!entitySystem.IsInsideMap(targetCell) || entitySystem.IsWall(targetCell))
            return false;

        target = entitySystem.GetOccupant(targetCell);
        if (!entitySystem.IsValid(target))
            return false;

        targetIndex = entitySystem.GetIndex(target);
        return targetIndex >= 0 && entitySystem.entities.coreComponents[targetIndex].EntityType == EntityType.Enemy;
    }

    private static bool TryGetEdgeBounceDestination(EntitySystem entitySystem, Vector2Int boxPosition, out Vector2Int destination)
    {
        destination = default;
        if (!IsBoundaryCell(entitySystem, boxPosition))
            return false;

        bool found = false;
        int bestDistanceSq = int.MaxValue;
        int bestCenterDistance = int.MaxValue;
        int width = entitySystem.entities.mapWidth;
        int height = entitySystem.entities.mapHeight;

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                Vector2Int candidate = boxPosition + new Vector2Int(dx, dy);
                if (!IsValidBounceDestination(entitySystem, candidate))
                    continue;

                int distanceSq = dx * dx + dy * dy;
                int centerDistance = Mathf.Abs(candidate.x * 2 - (width - 1)) + Mathf.Abs(candidate.y * 2 - (height - 1));
                if (!found ||
                    distanceSq < bestDistanceSq ||
                    (distanceSq == bestDistanceSq && centerDistance < bestCenterDistance))
                {
                    found = true;
                    bestDistanceSq = distanceSq;
                    bestCenterDistance = centerDistance;
                    destination = candidate;
                }
            }
        }

        return found;
    }

    private static bool IsValidBounceDestination(EntitySystem entitySystem, Vector2Int cell)
    {
        return entitySystem.IsInsideMap(cell) &&
               !IsBoundaryCell(entitySystem, cell) &&
               !entitySystem.IsWall(cell) &&
               !entitySystem.IsValid(entitySystem.GetOccupant(cell));
    }

    private static bool IsBoundaryCell(EntitySystem entitySystem, Vector2Int cell)
    {
        int width = entitySystem.entities.mapWidth;
        int height = entitySystem.entities.mapHeight;
        return cell.x <= 0 || cell.y <= 0 || cell.x >= width - 1 || cell.y >= height - 1;
    }

    private static bool IsLockedCoreBox(EntitySystem entitySystem, EntityHandle box)
    {
        int index = entitySystem.GetIndex(box);
        return index >= 0 &&
               entitySystem.entities.propertyComponents[index].IsCore &&
               EnemyAutoAISystem.IsCoreBoxMovementLocked(box);
    }

    private static void MoveBox(EntitySystem entitySystem, EntityHandle box, Vector2Int destination)
    {
        int index = entitySystem.GetIndex(box);
        if (index < 0)
            return;

        var entities = entitySystem.entities;
        Vector2Int current = entities.coreComponents[index].Position;
        int currentMapIndex = current.y * entities.mapWidth + current.x;
        int nextMapIndex = destination.y * entities.mapWidth + destination.x;

        if (entities.coreComponents[index].OccupiesGrid && entities.gridMap[currentMapIndex] == box.Id)
            entities.gridMap[currentMapIndex] = -1;

        if (entities.coreComponents[index].OccupiesGrid)
            entities.gridMap[nextMapIndex] = box.Id;

        entities.coreComponents[index].Position = destination;

        EventBusSystem.Instance?.Publish(new StageEvent(
            StageEventType.EntityMoved,
            actor: box,
            entity: box,
            entityType: EntityType.Box,
            from: current,
            to: destination,
            sourceTagId: entities.propertyComponents[index].SourceTagId));
    }
}
