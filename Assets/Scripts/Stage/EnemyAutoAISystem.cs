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
    private int[] _seekDistanceField;
    private int[] _activeBuildField;
    private readonly List<int> _openSet = new();
    private readonly HashSet<int> _reservedMoveCells = new();
    private readonly Dictionary<int, int> _coreLocksByCasterId = new();
    private static readonly HashSet<int> LockedCoreBoxIds = new();
    private int _guokuiTagId = -1;
    private EntityBP _guokuiBP;
    private readonly EnemyProfileResolver _profileResolver = new();

    private enum EnemyActionType
    {
        None,
        Move,
        AttackWall,
        AttackOccupant
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
        _profileResolver.ConfigureSpecialEnemyTags(grenadierTagId, crossbowTagId, artilleryTagId);
    }

    public void ConfigureAdvancedEnemyTags(int curseCasterTagId, int guokuiTagId, int ertongTagId, EntityBP guokuiBP)
    {
        _guokuiTagId = guokuiTagId;
        _guokuiBP = guokuiBP;
        _profileResolver.ConfigureAdvancedEnemyTags(curseCasterTagId, guokuiTagId, ertongTagId);
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

        if (!TryFindAggroTarget(entitySystem, out var aggroTarget))
            return;

        RefreshCurseLocks(entitySystem);
        ProcessErtongSplits(entitySystem);
        EnsureDistanceFields(entitySystem.entities);
        BuildDistanceField(entitySystem, aggroTarget.Position, _wallBreakDistanceField, true);
        BuildDistanceField(entitySystem, aggroTarget.Position, _noWallBreakDistanceField, false);
        WriteEnemyIntents(entitySystem, intentSystem, aggroTarget);
    }

    private bool TryFindAggroTarget(EntitySystem entitySystem, out EnemyAggroTarget target)
    {
        var entities = entitySystem.entities;
        int playerIndex = -1;
        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var core = ref entities.coreComponents[i];
            if (core.EntityType == EntityType.Player && playerIndex < 0)
                playerIndex = i;

            if (core.EntityType != EntityType.Box || !entities.propertyComponents[i].IsCore)
                continue;

            target = new EnemyAggroTarget(
                entitySystem.GetHandleFromId(core.Id),
                core.Position,
                core.EntityType,
                true);
            return true;
        }

        if (playerIndex >= 0)
        {
            var player = entities.coreComponents[playerIndex];
            target = new EnemyAggroTarget(
                entitySystem.GetHandleFromId(player.Id),
                player.Position,
                player.EntityType,
                false);
            return true;
        }

        target = default;
        return false;
    }

    private void EnsureDistanceFields(EntityComponents entities)
    {
        int size = entities.mapWidth * entities.mapHeight;
        if (_wallBreakDistanceField == null || _wallBreakDistanceField.Length != size)
            _wallBreakDistanceField = new int[size];

        if (_noWallBreakDistanceField == null || _noWallBreakDistanceField.Length != size)
            _noWallBreakDistanceField = new int[size];

        if (_seekDistanceField == null || _seekDistanceField.Length != size)
            _seekDistanceField = new int[size];
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

        if (entityType == EntityType.Player)
            return 1;

        return -1;
    }

    private void WriteEnemyIntents(
        EntitySystem entitySystem,
        IntentSystem intentSystem,
        EnemyAggroTarget aggroTarget)
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
            EnemyProfile profile = _profileResolver.Resolve(props, rangedAttackRange);
            int[] movementField = profile.CanBreakWalls ? _wallBreakDistanceField : _noWallBreakDistanceField;
            var context = new EnemyDecisionContext(
                entitySystem,
                enemyHandle,
                i,
                core.Position,
                props,
                entities.statusComponents[i],
                aggroTarget,
                profile,
                movementField);

            if (TryRunDecisionPipeline(context, out var decision))
                CommitDecision(intentSystem, context, decision);
        }
    }

    private bool TryRunDecisionPipeline(in EnemyDecisionContext context, out EnemyDecision decision)
    {
        if (TrySpecialBeforeNormal(context, out decision))
            return true;

        if (AllowAttack && TryAttackBlockingPlayer(context, out decision))
            return true;

        if (AllowAttack && TryAttackAggroTarget(context, out decision))
            return true;

        if (AllowAttack && TryMoveToFutureAttackPoint(context, out decision))
            return true;

        if (TryChaseOrBreakWall(context, out decision))
            return true;

        decision = default;
        return false;
    }

    private bool TrySpecialBeforeNormal(in EnemyDecisionContext context, out EnemyDecision decision)
    {
        if (context.Profile.SpecialKind == EnemySpecialKind.CurseLock &&
            context.AggroTarget.IsCoreBox &&
            TryMaintainCurseLock(context.EntitySystem, context.EntityId, context.Position, context.AggroTarget.Position))
        {
            decision = EnemyDecision.Handled("curse-lock");
            return true;
        }

        decision = default;
        return false;
    }

    private bool TryAttackBlockingPlayer(in EnemyDecisionContext context, out EnemyDecision decision)
    {
        if (TryFindBlockingPlayerTarget(context.EntitySystem, context.Position, context.MovementField, out var blockingPlayerPosition) &&
            TryBuildAttackDecision(context, blockingPlayerPosition, out decision))
        {
            return true;
        }

        decision = default;
        return false;
    }

    private bool TryAttackAggroTarget(in EnemyDecisionContext context, out EnemyDecision decision)
    {
        return TryBuildAttackDecision(context, context.AggroTarget.Position, out decision);
    }

    private bool TryMoveToFutureAttackPoint(in EnemyDecisionContext context, out EnemyDecision decision)
    {
        decision = default;
        if (!context.Profile.UsesFutureAttackStandPoint)
            return false;

        BuildDistanceField(context.EntitySystem, context.Position, _seekDistanceField, context.Profile.CanBreakWalls);
        if (!TryFindNearestAttackStandPoint(context.EntitySystem, context.Position, context.Profile, context.AggroTarget.Position, _seekDistanceField, out var standPoint))
            return false;

        if (standPoint == context.Position)
            return false;

        Vector2Int direction = GetStepTowardPoint(context.EntitySystem, context.Position, standPoint, _seekDistanceField);
        if (direction == Vector2Int.zero)
            return false;

        int distance = Mathf.Max(1, Mathf.Min(context.Profile.MoveDistance, 1));
        decision = EnemyDecision.Move(
            direction,
            distance,
            context.Position + direction * distance,
            "move-to-attack-standpoint");
        return true;
    }

    private bool TryChaseOrBreakWall(in EnemyDecisionContext context, out EnemyDecision decision)
    {
        decision = default;
        if (!TryGetNextAction(
                context.EntitySystem,
                context.EntityId,
                context.Position,
                context.MovementField,
                context.Profile.CanBreakWalls,
                context.Profile.MoveDistance,
                out var actionType,
                out var direction,
                out var actionDistance))
        {
            return false;
        }

        Vector2Int targetPosition = context.Position + direction;
        switch (actionType)
        {
            case EnemyActionType.AttackWall:
                if (!TryEnsureWallAttackTarget(context.EntitySystem, targetPosition))
                    return false;

                decision = EnemyDecision.Attack(EnemyAttackPayload.Single(targetPosition), "attack-wall");
                return true;

            case EnemyActionType.AttackOccupant:
                decision = EnemyDecision.Attack(EnemyAttackPayload.Single(targetPosition), "attack-occupant");
                return true;

            case EnemyActionType.Move:
                decision = EnemyDecision.Move(
                    direction,
                    actionDistance,
                    context.Position + direction * actionDistance,
                    "distance-field-move");
                return true;

            default:
                return false;
        }
    }

    private void CommitDecision(IntentSystem intentSystem, in EnemyDecisionContext context, in EnemyDecision decision)
    {
        switch (decision.Kind)
        {
            case EnemyDecisionKind.HandledWithoutIntent:
                return;

            case EnemyDecisionKind.Attack:
            {
                if (decision.AttackPayload.TargetCount <= 0)
                    return;

                var attackIntent = intentSystem.Request<AttackIntent>();
                attackIntent.Setup(decision.AttackPayload.TargetPositions[0]);
                attackIntent.DamageMultipliers[0] = decision.AttackPayload.DamageMultipliers[0];
                for (int i = 1; i < decision.AttackPayload.TargetCount; i++)
                    attackIntent.AddTarget(decision.AttackPayload.TargetPositions[i], decision.AttackPayload.DamageMultipliers[i]);

                intentSystem.SetIntent(context.Actor, IntentType.Attack, attackIntent);
                return;
            }

            case EnemyDecisionKind.Move:
            {
                var moveIntent = intentSystem.Request<MoveIntent>();
                moveIntent.Setup(decision.MoveDirection, decision.MoveDistance);
                intentSystem.SetIntent(context.Actor, IntentType.Move, moveIntent);
                if (decision.HasReservedDestination)
                    _reservedMoveCells.Add(ToMapIndex(context.EntitySystem.entities, decision.ReservedDestination));

                return;
            }
        }
    }

    private bool TryFindBlockingPlayerTarget(
        EntitySystem entitySystem,
        Vector2Int enemyPosition,
        int[] movementField,
        out Vector2Int playerPosition)
    {
        playerPosition = default;
        if (movementField == null)
            return false;

        var entities = entitySystem.entities;
        int enemyDistance = GetDistanceFieldValue(entities, movementField, enemyPosition);
        if (enemyDistance <= 0)
            return false;

        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            Vector2Int candidate = entities.coreComponents[i].Position;
            int playerDistance = GetDistanceFieldValue(entities, movementField, candidate);
            if (playerDistance >= 0 && playerDistance < enemyDistance)
            {
                playerPosition = candidate;
                return true;
            }
        }

        return false;
    }

    private static int GetDistanceFieldValue(EntityComponents entities, int[] distanceField, Vector2Int position)
    {
        int index = ToMapIndex(entities, position);
        return index >= 0 && index < distanceField.Length ? distanceField[index] : -1;
    }

    private bool TryBuildAttackDecision(in EnemyDecisionContext context, Vector2Int targetPosition, out EnemyDecision decision)
    {
        decision = default;
        switch (context.Profile.AttackPattern)
        {
            case EnemyAttackPattern.AdjacentSingleCell:
                if (!IsCrossAdjacent(context.Position, targetPosition))
                    return false;

                decision = EnemyDecision.Attack(EnemyAttackPayload.Single(targetPosition), "adjacent-attack");
                return true;

            case EnemyAttackPattern.LineSingleCellRequiresWall:
                if (!CanUseLineAttack(context.EntitySystem, context.Position, targetPosition, context.Profile.AttackRange))
                    return false;

                decision = EnemyDecision.Attack(EnemyAttackPayload.Single(targetPosition), "line-attack");
                return true;

            case EnemyAttackPattern.LineSquare2x2RequiresWall:
                if (!TryBuildArtilleryAttackPayload(context.EntitySystem, context.Position, targetPosition, context.Profile.AttackRange, out var payload))
                    return false;

                decision = EnemyDecision.Attack(payload, "artillery-square-attack");
                return true;

            default:
                return false;
        }
    }

    private bool TryFindNearestAttackStandPoint(
        EntitySystem entitySystem,
        Vector2Int enemyPosition,
        EnemyProfile profile,
        Vector2Int targetPosition,
        int[] seekDistanceField,
        out Vector2Int standPoint)
    {
        standPoint = default;
        if (seekDistanceField == null)
            return false;

        int bestTravel = int.MaxValue;
        int bestScore = int.MaxValue;
        bool found = false;
        var entities = entitySystem.entities;

        for (int i = 0; i < seekDistanceField.Length; i++)
        {
            int travel = seekDistanceField[i];
            if (travel < 0 || travel > bestTravel)
                continue;

            Vector2Int pos = FromMapIndex(entities, i);
            if (!CanAttackFromPosition(entitySystem, pos, profile, targetPosition))
                continue;

            int score = travel * 100 + ManhattanDistance(pos, targetPosition) * 8;
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestTravel = travel;
            standPoint = pos;
            found = true;
        }

        return found;
    }

    private bool CanAttackFromPosition(EntitySystem entitySystem, Vector2Int enemyPosition, EnemyProfile profile, Vector2Int targetPosition)
    {
        return profile.AttackPattern switch
        {
            EnemyAttackPattern.AdjacentSingleCell => IsCrossAdjacent(enemyPosition, targetPosition),
            EnemyAttackPattern.LineSingleCellRequiresWall => CanUseLineAttack(entitySystem, enemyPosition, targetPosition, profile.AttackRange),
            EnemyAttackPattern.LineSquare2x2RequiresWall => CanUseLineAttack(entitySystem, enemyPosition, targetPosition, profile.AttackRange),
            _ => false
        };
    }

    private Vector2Int GetStepTowardPoint(EntitySystem entitySystem, Vector2Int from, Vector2Int to, int[] distanceField)
    {
        Vector2Int bestDirection = Vector2Int.zero;
        int bestField = int.MaxValue;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < Directions.Length; i++)
        {
            Vector2Int next = from + Directions[i];
            if (!entitySystem.IsInsideMap(next) || entitySystem.IsWall(next) || entitySystem.GetOccupantId(next) >= 0)
                continue;

            int index = ToMapIndex(entitySystem.entities, next);
            int field = distanceField[index];
            if (field < 0)
                continue;

            int manhattan = ManhattanDistance(next, to);
            if (field > bestField || (field == bestField && manhattan >= bestDistance))
                continue;

            bestField = field;
            bestDistance = manhattan;
            bestDirection = Directions[i];
        }

        return bestDirection;
    }

    private static int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
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
            if (casterIndex < 0 ||
                _profileResolver.Resolve(entitySystem.entities.propertyComponents[casterIndex], rangedAttackRange).SpecialKind != EnemySpecialKind.CurseLock)
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
            if (entities.coreComponents[i].EntityType != EntityType.Enemy)
                continue;

            EnemyProfile profile = _profileResolver.Resolve(entities.propertyComponents[i], rangedAttackRange);
            if (profile.SpecialKind != EnemySpecialKind.SplitIntoGuokui)
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

    private bool TryBuildArtilleryAttackPayload(
        EntitySystem entitySystem,
        Vector2Int enemyPosition,
        Vector2Int corePosition,
        int range,
        out EnemyAttackPayload payload)
    {
        payload = default;
        if (!CanUseLineAttack(entitySystem, enemyPosition, corePosition, range))
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
            if (TryBuildSquareAttackPayload(entitySystem, origin, out payload))
                return true;
        }

        return false;
    }

    private bool TryBuildSquareAttackPayload(EntitySystem entitySystem, Vector2Int origin, out EnemyAttackPayload payload)
    {
        payload = default;
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

        for (int i = 0; i < cells.Length; i++)
            payload.AddTarget(cells[i], 1f);

        return true;
    }

    private bool CanUseLineAttack(EntitySystem entitySystem, Vector2Int from, Vector2Int to, int range)
    {
        if (!IsInCrossRange(from, to, range))
            return false;

        Vector2Int direction = GetCrossDirection(from, to);
        return direction != Vector2Int.zero && HasWallBetween(entitySystem, from, to, direction);
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
                + (candidateAction != EnemyActionType.Move ? 8 : CountEnemyNeighbors(entitySystem, next, enemyId) * 16)
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
        return entityType switch
        {
            EntityType.Wall => EnemyActionType.AttackWall,
            EntityType.Player => EnemyActionType.AttackOccupant,
            _ => EnemyActionType.None
        };
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
