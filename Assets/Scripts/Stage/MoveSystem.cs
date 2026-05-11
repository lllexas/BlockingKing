using UnityEngine;

/// <summary>
/// 移动执行 + 移动相关特效/音效逻辑。
/// </summary>
public class MoveSystem : MonoBehaviour
{
    public static MoveSystem Instance { get; private set; }

    [SerializeField] private int maxPushChain = 1;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool Execute(EntityHandle actor, MoveIntent intent)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsValid(actor) || intent == null || !intent.Active)
            return false;

        var direction = intent.Direction;
        if (direction == Vector2Int.zero)
            return false;

        int index = entitySystem.GetIndex(actor);
        if (index < 0)
            return false;

        int steps = Mathf.Max(1, intent.Distance);
        for (int i = 0; i < steps; i++)
        {
            if (!TryMoveOneStep(actor, direction))
                break;
        }

        return true;
    }

    private bool TryMoveOneStep(EntityHandle actor, Vector2Int direction)
    {
        var entitySystem = EntitySystem.Instance;
        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        var entities = entitySystem.entities;
        var current = entities.coreComponents[actorIndex].Position;
        var next = current + direction;

        if (!entitySystem.IsInsideMap(next) || entitySystem.IsWall(next))
            return false;

        int occupantId = entitySystem.GetOccupantId(next);
        if (occupantId < 0)
        {
            MoveEntity(actor, next);
            return true;
        }

        var occupant = entitySystem.GetHandleFromId(occupantId);
        if (!TryPush(occupant, direction, maxPushChain))
            return false;

        MoveEntity(actor, next);
        return true;
    }

    private bool TryPush(EntityHandle actor, Vector2Int direction, int remainingPushChain)
    {
        if (remainingPushChain <= 0)
            return false;

        var entitySystem = EntitySystem.Instance;
        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        var entities = entitySystem.entities;
        ref var core = ref entities.coreComponents[actorIndex];
        if (!CanPush(core.EntityType))
            return false;

        if (core.EntityType == EntityType.Box &&
            entities.propertyComponents[actorIndex].IsCore &&
            EnemyAutoAISystem.IsCoreBoxMovementLocked(actor))
        {
            return false;
        }

        var next = core.Position + direction;
        if (!entitySystem.IsInsideMap(next) || entitySystem.IsWall(next))
            return false;

        int occupantId = entitySystem.GetOccupantId(next);
        if (occupantId >= 0)
        {
            var occupant = entitySystem.GetHandleFromId(occupantId);
            if (!TryPush(occupant, direction, remainingPushChain - 1))
                return false;
        }

        MoveEntity(actor, next);
        return true;
    }

    private static bool CanPush(EntityType entityType)
    {
        return entityType == EntityType.Box;
    }

    private static void MoveEntity(EntityHandle actor, Vector2Int next)
    {
        var entitySystem = EntitySystem.Instance;
        int index = entitySystem.GetIndex(actor);
        if (index < 0)
            return;

        var entities = entitySystem.entities;
        var current = entities.coreComponents[index].Position;

        int currentMapIndex = ToMapIndex(entities, current);
        int nextMapIndex = ToMapIndex(entities, next);

        if (entities.coreComponents[index].OccupiesGrid && entities.gridMap[currentMapIndex] == actor.Id)
            entities.gridMap[currentMapIndex] = -1;

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

    private static int ToMapIndex(EntityComponents entities, Vector2Int pos)
    {
        return pos.y * entities.mapWidth + pos.x;
    }
}
