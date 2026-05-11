using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌人自动决策。Tick 中读取当前场面，为敌人写入下一拍意图。
/// </summary>
public class EnemyAutoAISystem : MonoBehaviour, ITickSystem
{
    public static EnemyAutoAISystem Instance { get; private set; }

    [HideInInspector] public bool AllowAttack = true;
    [SerializeField, Min(1)] private int materializedTerrainWallHealth = 3;
    [SerializeField, Min(1)] private int rangedAttackRange = 6;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private int[] _wallBreakDistanceField;
    private int[] _noWallBreakDistanceField;
    private int[] _activeBuildField;
    private readonly List<int> _openSet = new();
    private readonly HashSet<int> _reservedMoveCells = new();
    private readonly Dictionary<int, int> _coreLocksByCasterId = new();
    private static readonly HashSet<int> LockedCoreBoxIds = new();
    private int _grenadierTagId = -1;
    private int _crossbowTagId = -1;
    private int _artilleryTagId = -1;
    private int _curseCasterTagId = -1;
    private int _guokuiTagId = -1;
    private int _ertongTagId = -1;
    private EntityBP _guokuiBP;

    private enum EnemyActionType
    {
        None,
        Move,
        AttackWall
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        _coreLocksByCasterId.Clear();
        LockedCoreBoxIds.Clear();
    }

    public void ConfigureMaterializedTerrainWallHealth(int health)
    {
        materializedTerrainWallHealth = Mathf.Max(1, health);
    }

    public void ConfigureSpecialEnemyTags(int grenadierTagId, int crossbowTagId, int artilleryTagId)
    {
        _grenadierTagId = grenadierTagId;
        _crossbowTagId = crossbowTagId;
        _artilleryTagId = artilleryTagId;
    }

    public void ConfigureAdvancedEnemyTags(int curseCasterTagId, int guokuiTagId, int ertongTagId, EntityBP guokuiBP)
    {
        _curseCasterTagId = curseCasterTagId;
        _guokuiTagId = guokuiTagId;
        _ertongTagId = ertongTagId;
        _guokuiBP = guokuiBP;
    }

    public static bool IsCoreBoxMovementLocked(EntityHandle coreBox)
    {
        return coreBox.Id >= 0 && LockedCoreBoxIds.Contains(coreBox.Id);
    }

    public void Tick()
    {
        var entitySystem = EntitySystem.Instance;
        var intentSystem = IntentSystem.Instance;
        if (entitySystem == null || intentSystem == null || !entitySystem.IsInitialized)
            return;

        if (!TryFindCoreBox(entitySystem, out var coreHandle, out var corePosition))
            return;

        RefreshCurseLocks(entitySystem);
        ProcessErtongSplits(entitySystem);
        EnsureDistanceFields(entitySystem.entities);
        BuildDistanceField(entitySystem, corePosition, _wallBreakDistanceField, true);
        BuildDistanceField(entitySystem, corePosition, _noWallBreakDistanceField, false);
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

    private void EnsureDistanceFields(EntityComponents entities)
    {
        int size = entities.mapWidth * entities.mapHeight;
        if (_wallBreakDistanceField == null || _wallBreakDistanceField.Length != size)
            _wallBreakDistanceField = new int[size];

        if (_noWallBreakDistanceField == null || _noWallBreakDistanceField.Length != size)
            _noWallBreakDistanceField = new int[size];
    }

    private void BuildDistanceField(EntitySystem entitySystem, Vector2Int corePosition, int[] distanceField, bool allowWallBreak)
    {
        var entities = entitySystem.entities;
        for (int i = 0; i < distanceField.Length; i++)
            distanceField[i] = -1;

        _activeBuildField = distanceField;
        _openSet.Clear();

        int coreMapIndex = ToMapIndex(entities, corePosition);
        if (coreMapIndex < 0 || coreMapIndex >= distanceField.Length)
            return;

        distanceField[coreMapIndex] = 0;
        _openSet.Add(coreMapIndex);

        while (_openSet.Count > 0)
        {
            int currentMapIndex = PopLowestDistanceOpenNode();
            Vector2Int current = FromMapIndex(entities, currentMapIndex);
            int currentDistance = distanceField[currentMapIndex];

            for (int i = 0; i < Directions.Length; i++)
            {
                Vector2Int next = current + Directions[i];
                if (!entitySystem.IsInsideMap(next))
                    continue;

                int nextMapIndex = ToMapIndex(entities, next);
                int enterCost = GetFlowEnterCost(entitySystem, next, corePosition, allowWallBreak);
                if (enterCost < 0)
                    continue;

                int nextDistance = currentDistance + enterCost;
                if (distanceField[nextMapIndex] >= 0 && distanceField[nextMapIndex] <= nextDistance)
                    continue;

                distanceField[nextMapIndex] = nextDistance;
                if (!_openSet.Contains(nextMapIndex))
                    _openSet.Add(nextMapIndex);
            }
        }
    }

    private int GetFlowEnterCost(EntitySystem entitySystem, Vector2Int pos, Vector2Int corePosition, bool allowWallBreak)
    {
        if (pos == corePosition)
            return 1;

        if (entitySystem.IsWall(pos))
            return allowWallBreak ? Mathf.Max(2, materializedTerrainWallHealth + 1) : -1;

        int occupantId = entitySystem.GetOccupantId(pos);
        if (occupantId < 0)
            return 1;

        int index = entitySystem.GetIndex(entitySystem.GetHandleFromId(occupantId));
        if (index < 0)
            return -1;

        var entities = entitySystem.entities;
        EntityType entityType = entities.coreComponents[index].EntityType;
        if (entityType == EntityType.Enemy)
            return 1;

        if (entityType == EntityType.Wall)
            return allowWallBreak ? Mathf.Max(2, CombatStats.GetCurrentHealth(entities.statusComponents[index]) + 1) : -1;

        return -1;
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
            ref var props = ref entities.propertyComponents[i];
            bool isGrenadier = IsEnemyKind(props, _grenadierTagId, "Grenadier");
            bool isCrossbow = IsEnemyKind(props, _crossbowTagId, "Crossbow", "Arbalest");
            bool isArtillery = IsEnemyKind(props, _artilleryTagId, "Artillery", "Cannon");
            bool isCurseCaster = IsEnemyKind(props, _curseCasterTagId, "Curse", "Caster", "Sorcerer");
            bool isGuokui = IsEnemyKind(props, _guokuiTagId, "Guokui");
            bool isErtong = IsEnemyKind(props, _ertongTagId, "Ertong");
            bool canBreakWalls = !isGrenadier && !isArtillery && !isCurseCaster;
            int moveDistance = isErtong ? 4 : isGuokui ? 2 : 1;

            if (isCurseCaster && TryMaintainCurseLock(entitySystem, core.Id, core.Position, corePosition))
                continue;

            if (AllowAttack && TryBuildSpecialAttack(entitySystem, core.Position, props, corePosition, out var specialAttackIntent))
            {
                intentSystem.SetIntent(enemyHandle, IntentType.Attack, specialAttackIntent);
                continue;
            }

            if (AllowAttack && !isGrenadier && !isCrossbow && !isArtillery && !isCurseCaster && IsCrossAdjacent(core.Position, corePosition))
            {
                var attackIntent = intentSystem.Request<AttackIntent>();
                attackIntent.Setup(corePosition);
                intentSystem.SetIntent(enemyHandle, IntentType.Attack, attackIntent);
                continue;
            }

            int[] movementField = canBreakWalls ? _wallBreakDistanceField : _noWallBreakDistanceField;
            if (!TryGetNextAction(entitySystem, core.Id, core.Position, movementField, canBreakWalls, moveDistance, out var actionType, out var direction, out var actionDistance))
                continue;

            Vector2Int targetPosition = core.Position + direction;
            if (actionType == EnemyActionType.AttackWall)
            {
                if (!TryEnsureWallAttackTarget(entitySystem, targetPosition))
                    continue;

                var attackIntent = intentSystem.Request<AttackIntent>();
                attackIntent.Setup(targetPosition);
                intentSystem.SetIntent(enemyHandle, IntentType.Attack, attackIntent);
                continue;
            }

            if (actionType == EnemyActionType.Move)
            {
                var moveIntent = intentSystem.Request<MoveIntent>();
                moveIntent.Setup(direction, actionDistance);
                intentSystem.SetIntent(enemyHandle, IntentType.Move, moveIntent);
                _reservedMoveCells.Add(ToMapIndex(entities, core.Position + direction * actionDistance));
            }
        }
    }

    private bool TryBuildSpecialAttack(
        EntitySystem entitySystem,
        Vector2Int enemyPosition,
        PropertyComponent props,
        Vector2Int corePosition,
        out AttackIntent attackIntent)
    {
        attackIntent = null;

        if (IsEnemyKind(props, _grenadierTagId, "Grenadier") ||
            IsEnemyKind(props, _crossbowTagId, "Crossbow", "Arbalest"))
        {
            if (!IsInCrossRange(enemyPosition, corePosition, rangedAttackRange))
                return false;

            attackIntent = IntentSystem.Instance.Request<AttackIntent>();
            attackIntent.Setup(corePosition);
            return true;
        }

        if (IsEnemyKind(props, _artilleryTagId, "Artillery", "Cannon"))
            return TryBuildArtilleryAttack(entitySystem, enemyPosition, corePosition, out attackIntent);

        return false;
    }

    private void RefreshCurseLocks(EntitySystem entitySystem)
    {
        LockedCoreBoxIds.Clear();
        var staleCasterIds = new List<int>();

        foreach (var pair in _coreLocksByCasterId)
        {
            var caster = entitySystem.GetHandleFromId(pair.Key);
            if (!entitySystem.IsValid(caster))
            {
                staleCasterIds.Add(pair.Key);
                continue;
            }

            int casterIndex = entitySystem.GetIndex(caster);
            if (casterIndex < 0 || !IsEnemyKind(entitySystem.entities.propertyComponents[casterIndex], _curseCasterTagId, "Curse", "Caster", "Sorcerer"))
            {
                staleCasterIds.Add(pair.Key);
                continue;
            }

            var coreHandle = entitySystem.GetHandleFromId(pair.Value);
            if (!entitySystem.IsValid(coreHandle))
            {
                staleCasterIds.Add(pair.Key);
                continue;
            }

            LockedCoreBoxIds.Add(pair.Value);
        }

        for (int i = 0; i < staleCasterIds.Count; i++)
            _coreLocksByCasterId.Remove(staleCasterIds[i]);
    }

    private bool TryMaintainCurseLock(EntitySystem entitySystem, int casterId, Vector2Int casterPosition, Vector2Int corePosition)
    {
        if (_coreLocksByCasterId.ContainsKey(casterId))
            return true;

        if (!IsDiagonalRange(casterPosition, corePosition, 3))
            return false;

        var coreHandle = entitySystem.GetOccupant(corePosition);
        if (!entitySystem.IsValid(coreHandle))
            return false;

        _coreLocksByCasterId[casterId] = coreHandle.Id;
        LockedCoreBoxIds.Add(coreHandle.Id);
        Debug.Log($"[EnemyAutoAISystem] Core box locked by curse caster id={casterId}.");
        return true;
    }

    private void ProcessErtongSplits(EntitySystem entitySystem)
    {
        var entities = entitySystem.entities;
        for (int i = entities.entityCount - 1; i >= 0; i--)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Enemy ||
                !IsEnemyKind(entities.propertyComponents[i], _ertongTagId, "Ertong"))
            {
                continue;
            }

            int maxHealth = CombatStats.GetMaxHealth(entities.statusComponents[i]);
            int currentHealth = CombatStats.GetCurrentHealth(entities.statusComponents[i]);
            if (currentHealth > maxHealth / 2)
                continue;

            Vector2Int pos = entities.coreComponents[i].Position;
            var handle = entitySystem.GetHandleFromId(entities.coreComponents[i].Id);
            entitySystem.DestroyEntity(handle);
            SpawnGuokui(entitySystem, pos);

            if (TryFindSplitCell(entitySystem, pos, out var secondPos))
                SpawnGuokui(entitySystem, secondPos);
        }
    }

    private bool TryFindSplitCell(EntitySystem entitySystem, Vector2Int origin, out Vector2Int cell)
    {
        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int candidate = origin + Directions[i];
            if (entitySystem.IsInsideMap(candidate) &&
                !entitySystem.IsWall(candidate) &&
                entitySystem.GetOccupantId(candidate) < 0)
            {
                cell = candidate;
                return true;
            }
        }

        cell = default;
        return false;
    }

    private void SpawnGuokui(EntitySystem entitySystem, Vector2Int pos)
    {
        if (!entitySystem.IsInsideMap(pos) || entitySystem.IsWall(pos) || entitySystem.GetOccupantId(pos) >= 0)
            return;

        var handle = entitySystem.CreateEntity(EntityType.Enemy, pos);
        if (!entitySystem.IsValid(handle))
            return;

        ApplyEnemyBP(entitySystem, handle, _guokuiBP, _guokuiTagId);
        entitySystem.PublishEntityCreated(handle);
        Debug.Log($"[EnemyAutoAISystem] Ertong split spawned Guokui at {pos}.");
    }

    private static void ApplyEnemyBP(EntitySystem entitySystem, EntityHandle handle, EntityBP bp, int sourceTagId)
    {
        int index = entitySystem.GetIndex(handle);
        if (index < 0)
            return;

        ref var properties = ref entitySystem.entities.propertyComponents[index];
        ref var status = ref entitySystem.entities.statusComponents[index];
        properties.SourceTagId = sourceTagId;
        properties.SourceBP = bp;

        if (bp == null)
            return;

        status.BaseMaxHealth = Mathf.Max(1, bp.health);
        status.BaseAttack = Mathf.Max(0, bp.attack);
        status.DamageTaken = 0;
        status.AttackModifier = 0;
        status.MaxHealthModifier = 0;
        properties.Attack = CombatStats.GetAttack(status);
    }

    private bool TryBuildArtilleryAttack(
        EntitySystem entitySystem,
        Vector2Int enemyPosition,
        Vector2Int corePosition,
        out AttackIntent attackIntent)
    {
        attackIntent = null;
        if (!IsInCrossRange(enemyPosition, corePosition, rangedAttackRange))
            return false;

        Vector2Int direction = GetCrossDirection(enemyPosition, corePosition);
        if (direction == Vector2Int.zero || !HasWallBetween(entitySystem, enemyPosition, corePosition, direction))
            return false;

        Vector2Int[] origins =
        {
            corePosition,
            corePosition + Vector2Int.left,
            corePosition + Vector2Int.down,
            corePosition + new Vector2Int(-1, -1)
        };

        int start = Mathf.Abs(enemyPosition.x * 31 + enemyPosition.y * 17 + entitySystem.GlobalTick) % origins.Length;
        for (int i = 0; i < origins.Length; i++)
        {
            Vector2Int origin = origins[(start + i) % origins.Length];
            if (TryBuildSquareAttack(entitySystem, origin, out attackIntent))
                return true;
        }

        return false;
    }

    private bool TryBuildSquareAttack(EntitySystem entitySystem, Vector2Int origin, out AttackIntent attackIntent)
    {
        attackIntent = null;
        Vector2Int[] cells =
        {
            origin,
            origin + Vector2Int.right,
            origin + Vector2Int.up,
            origin + Vector2Int.one
        };

        for (int i = 0; i < cells.Length; i++)
        {
            if (!entitySystem.IsInsideMap(cells[i]))
                return false;
        }

        attackIntent = IntentSystem.Instance.Request<AttackIntent>();
        attackIntent.Setup(cells[0]);
        for (int i = 1; i < cells.Length; i++)
            attackIntent.AddTarget(cells[i], 1f);

        return true;
    }

    private bool HasWallBetween(EntitySystem entitySystem, Vector2Int from, Vector2Int to, Vector2Int direction)
    {
        Vector2Int current = from + direction;
        while (current != to)
        {
            if (entitySystem.IsWall(current))
                return true;

            EntityHandle occupant = entitySystem.GetOccupant(current);
            if (entitySystem.IsValid(occupant))
            {
                int index = entitySystem.GetIndex(occupant);
                if (index >= 0 && entitySystem.entities.coreComponents[index].EntityType == EntityType.Wall)
                    return true;
            }

            current += direction;
        }

        return false;
    }

    private bool TryGetNextAction(
        EntitySystem entitySystem,
        int enemyId,
        Vector2Int current,
        int[] distanceField,
        bool allowWallBreak,
        int moveDistance,
        out EnemyActionType actionType,
        out Vector2Int direction,
        out int actionDistance)
    {
        var entities = entitySystem.entities;
        int currentMapIndex = ToMapIndex(entities, current);
        int currentDistance = currentMapIndex >= 0 && currentMapIndex < distanceField.Length
            ? distanceField[currentMapIndex]
            : -1;

        actionType = EnemyActionType.None;
        direction = Vector2Int.zero;
        actionDistance = 0;
        if (currentDistance < 0)
            return false;

        int bestScore = int.MaxValue;
        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int next = current + Directions[i];
            if (!entitySystem.IsInsideMap(next))
                continue;

            int nextMapIndex = ToMapIndex(entities, next);
            int nextDistance = distanceField[nextMapIndex];
            if (nextDistance < 0 || nextDistance >= currentDistance)
                continue;

            EnemyActionType candidateAction = GetActionForNextCell(entitySystem, next);
            if (candidateAction == EnemyActionType.None)
                continue;

            if (candidateAction == EnemyActionType.AttackWall && !allowWallBreak)
                continue;

            if (candidateAction == EnemyActionType.Move && _reservedMoveCells.Contains(nextMapIndex))
                continue;

            int score = nextDistance * 100
                + (candidateAction == EnemyActionType.AttackWall ? 8 : CountEnemyNeighbors(entitySystem, next, enemyId) * 16)
                + StableTieBreak(enemyId, i);

            if (score >= bestScore)
                continue;

            bestScore = score;
            actionType = candidateAction;
            direction = Directions[i];
            actionDistance = candidateAction == EnemyActionType.Move
                ? GetMoveDistance(entitySystem, current, Directions[i], distanceField, Mathf.Max(1, moveDistance))
                : 1;
        }

        return actionType != EnemyActionType.None && direction != Vector2Int.zero && actionDistance > 0;
    }

    private int GetMoveDistance(EntitySystem entitySystem, Vector2Int current, Vector2Int direction, int[] distanceField, int maxDistance)
    {
        var entities = entitySystem.entities;
        int bestDistance = 1;
        int bestField = int.MaxValue;
        Vector2Int pos = current;
        for (int step = 1; step <= maxDistance; step++)
        {
            pos += direction;
            if (!entitySystem.IsInsideMap(pos) || entitySystem.IsWall(pos) || entitySystem.GetOccupantId(pos) >= 0)
                break;

            int mapIndex = ToMapIndex(entities, pos);
            int field = distanceField[mapIndex];
            if (field < 0 || field >= bestField)
                break;

            bestField = field;
            bestDistance = step;
        }

        return bestDistance;
    }

    private EnemyActionType GetActionForNextCell(EntitySystem entitySystem, Vector2Int next)
    {
        if (entitySystem.IsWall(next))
            return EnemyActionType.AttackWall;

        int occupantId = entitySystem.GetOccupantId(next);
        if (occupantId < 0)
            return EnemyActionType.Move;

        int index = entitySystem.GetIndex(entitySystem.GetHandleFromId(occupantId));
        if (index < 0)
            return EnemyActionType.None;

        EntityType entityType = entitySystem.entities.coreComponents[index].EntityType;
        return entityType == EntityType.Wall
            ? EnemyActionType.AttackWall
            : EnemyActionType.None;
    }

    private bool TryEnsureWallAttackTarget(EntitySystem entitySystem, Vector2Int targetPosition)
    {
        if (!entitySystem.IsInsideMap(targetPosition))
            return false;

        if (entitySystem.IsWall(targetPosition))
            return entitySystem.IsValid(entitySystem.TryMaterializeWall(targetPosition, materializedTerrainWallHealth));

        var occupant = entitySystem.GetOccupant(targetPosition);
        if (!entitySystem.IsValid(occupant))
            return false;

        int index = entitySystem.GetIndex(occupant);
        return index >= 0 && entitySystem.entities.coreComponents[index].EntityType == EntityType.Wall;
    }

    private int PopLowestDistanceOpenNode()
    {
        int bestListIndex = 0;
        int bestMapIndex = _openSet[0];
        int bestDistance = _activeBuildField[bestMapIndex];

        for (int i = 1; i < _openSet.Count; i++)
        {
            int mapIndex = _openSet[i];
            int distance = _activeBuildField[mapIndex];
            if (distance >= bestDistance)
                continue;

            bestListIndex = i;
            bestMapIndex = mapIndex;
            bestDistance = distance;
        }

        _openSet.RemoveAt(bestListIndex);
        return bestMapIndex;
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

    private static bool IsInCrossRange(Vector2Int from, Vector2Int to, int range)
    {
        if (from.x != to.x && from.y != to.y)
            return false;

        int distance = Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
        return distance > 0 && distance <= Mathf.Max(1, range);
    }

    private static bool IsDiagonalRange(Vector2Int from, Vector2Int to, int range)
    {
        int dx = Mathf.Abs(from.x - to.x);
        int dy = Mathf.Abs(from.y - to.y);
        return dx == dy && dx > 0 && dx <= Mathf.Max(1, range);
    }

    private static Vector2Int GetCrossDirection(Vector2Int from, Vector2Int to)
    {
        if (from.x == to.x)
            return to.y > from.y ? Vector2Int.up : Vector2Int.down;

        if (from.y == to.y)
            return to.x > from.x ? Vector2Int.right : Vector2Int.left;

        return Vector2Int.zero;
    }

    private static bool IsTag(int sourceTagId, int expectedTagId)
    {
        return expectedTagId > 0 && sourceTagId == expectedTagId;
    }

    private static bool IsEnemyKind(in PropertyComponent props, int expectedTagId, params string[] nameHints)
    {
        if (IsTag(props.SourceTagId, expectedTagId))
            return true;

        if (props.SourceBP == null || nameHints == null)
            return false;

        string bpName = props.SourceBP.name;
        if (string.IsNullOrEmpty(bpName))
            return false;

        for (int i = 0; i < nameHints.Length; i++)
        {
            if (!string.IsNullOrEmpty(nameHints[i]) &&
                bpName.IndexOf(nameHints[i], System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
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

    private static Vector2Int FromMapIndex(EntityComponents entities, int mapIndex)
    {
        return new Vector2Int(mapIndex % entities.mapWidth, mapIndex / entities.mapWidth);
    }
}
