using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌人自动决策。Tick 中读取当前场面，为敌人写入下一拍意图。
/// </summary>
public class EnemyAutoAISystem : MonoBehaviour, ITickSystem
{
    public static EnemyAutoAISystem Instance { get; private set; }

    [HideInInspector] public bool AllowAttack = true;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private int[] _distanceField;
    private readonly Queue<Vector2Int> _frontier = new();
    private readonly HashSet<int> _reservedMoveCells = new();

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Tick()
    {
        var entitySystem = EntitySystem.Instance;
        var intentSystem = IntentSystem.Instance;
        if (entitySystem == null || intentSystem == null || !entitySystem.IsInitialized)
            return;

        if (!TryFindCoreBox(entitySystem, out var coreHandle, out var corePosition))
            return;

        EnsureDistanceField(entitySystem.entities);
        BuildDistanceField(entitySystem, corePosition);
        WriteEnemyIntents(entitySystem, intentSystem, coreHandle, corePosition);
    }

    private bool TryFindCoreBox(EntitySystem entitySystem, out EntityHandle coreHandle, out Vector2Int corePosition)
    {
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Box)
                continue;

            if (!entities.propertyComponents[i].IsCore)
                continue;

            coreHandle = entitySystem.GetHandleFromId(entities.coreComponents[i].Id);
            corePosition = entities.coreComponents[i].Position;
            return true;
        }

        coreHandle = EntityHandle.None;
        corePosition = Vector2Int.zero;
        return false;
    }

    private void EnsureDistanceField(EntityComponents entities)
    {
        int size = entities.mapWidth * entities.mapHeight;
        if (_distanceField == null || _distanceField.Length != size)
            _distanceField = new int[size];
    }

    private void BuildDistanceField(EntitySystem entitySystem, Vector2Int corePosition)
    {
        var entities = entitySystem.entities;
        for (int i = 0; i < _distanceField.Length; i++)
            _distanceField[i] = -1;

        _frontier.Clear();

        int coreMapIndex = ToMapIndex(entities, corePosition);
        if (coreMapIndex < 0 || coreMapIndex >= _distanceField.Length)
            return;

        _distanceField[coreMapIndex] = 0;
        _frontier.Enqueue(corePosition);

        while (_frontier.Count > 0)
        {
            Vector2Int current = _frontier.Dequeue();
            int currentDistance = _distanceField[ToMapIndex(entities, current)];

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (!entitySystem.IsInsideMap(next))
                    continue;

                int nextMapIndex = ToMapIndex(entities, next);
                if (_distanceField[nextMapIndex] >= 0)
                    continue;

                if (!CanFlowThrough(entitySystem, next, corePosition))
                    continue;

                _distanceField[nextMapIndex] = currentDistance + 1;
                _frontier.Enqueue(next);
            }
        }
    }

    private bool CanFlowThrough(EntitySystem entitySystem, Vector2Int pos, Vector2Int corePosition)
    {
        if (pos == corePosition)
            return true;

        if (entitySystem.IsWall(pos))
            return false;

        int occupantId = entitySystem.GetOccupantId(pos);
        if (occupantId < 0)
            return true;

        int index = entitySystem.GetIndex(entitySystem.GetHandleFromId(occupantId));
        return index >= 0 && entitySystem.entities.coreComponents[index].EntityType == EntityType.Enemy;
    }

    private void WriteEnemyIntents(
        EntitySystem entitySystem,
        IntentSystem intentSystem,
        EntityHandle coreHandle,
        Vector2Int corePosition)
    {
        var entities = entitySystem.entities;
        _reservedMoveCells.Clear();

        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var core = ref entities.coreComponents[i];
            if (core.EntityType != EntityType.Enemy)
                continue;

            var enemyHandle = entitySystem.GetHandleFromId(core.Id);
            if (AllowAttack && IsCrossAdjacent(core.Position, corePosition))
            {
                var attackIntent = intentSystem.Request<AttackIntent>();
                attackIntent.Setup(corePosition);
                intentSystem.SetIntent(enemyHandle, IntentType.Attack, attackIntent);
                continue;
            }

            if (!TryGetNextMoveDirection(entitySystem, core.Id, core.Position, out var direction))
                continue;

            var moveIntent = intentSystem.Request<MoveIntent>();
            moveIntent.Setup(direction, 1);
            intentSystem.SetIntent(enemyHandle, IntentType.Move, moveIntent);
            _reservedMoveCells.Add(ToMapIndex(entities, core.Position + direction));
        }
    }

    private bool TryGetNextMoveDirection(EntitySystem entitySystem, int enemyId, Vector2Int current, out Vector2Int direction)
    {
        var entities = entitySystem.entities;
        int currentMapIndex = ToMapIndex(entities, current);
        int currentDistance = currentMapIndex >= 0 && currentMapIndex < _distanceField.Length
            ? _distanceField[currentMapIndex]
            : -1;

        direction = Vector2Int.zero;
        if (currentDistance < 0)
            return false;

        int bestScore = int.MaxValue;
        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int next = current + Directions[i];
            if (!entitySystem.IsInsideMap(next) || entitySystem.IsBlocked(next))
                continue;

            int nextMapIndex = ToMapIndex(entities, next);
            if (_reservedMoveCells.Contains(nextMapIndex))
                continue;

            int nextDistance = _distanceField[nextMapIndex];
            if (nextDistance < 0 || nextDistance >= currentDistance)
                continue;

            int score = nextDistance * 100
                + CountEnemyNeighbors(entitySystem, next, enemyId) * 16
                + StableTieBreak(enemyId, i);

            if (score >= bestScore)
                continue;

            bestScore = score;
            direction = Directions[i];
        }

        return direction != Vector2Int.zero;
    }

    private int CountEnemyNeighbors(EntitySystem entitySystem, Vector2Int center, int selfId)
    {
        int count = 0;
        var entities = entitySystem.entities;

        for (int y = -1; y <= 1; y++)
        {
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                Vector2Int pos = center + new Vector2Int(x, y);
                if (!entitySystem.IsInsideMap(pos))
                    continue;

                int occupantId = entitySystem.GetOccupantId(pos);
                if (occupantId < 0 || occupantId == selfId)
                    continue;

                int occupantIndex = entitySystem.GetIndex(entitySystem.GetHandleFromId(occupantId));
                if (occupantIndex >= 0 && entities.coreComponents[occupantIndex].EntityType == EntityType.Enemy)
                    count++;
            }
        }

        return count;
    }

    private static int StableTieBreak(int entityId, int directionIndex)
    {
        unchecked
        {
            int hash = entityId * 73856093 ^ directionIndex * 19349663;
            return Mathf.Abs(hash % 11);
        }
    }

    private static bool IsCrossAdjacent(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;
    }

    private static int ToMapIndex(EntityComponents entities, Vector2Int pos)
    {
        return pos.y * entities.mapWidth + pos.x;
    }
}
