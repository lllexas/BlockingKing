using UnityEngine;

public enum EnemyAttackPattern
{
    None,
    AdjacentSingleCell,
    LineSingleCellRequiresWall,
    LineSquare2x2RequiresWall
}

public enum EnemySpecialKind
{
    None,
    CurseLock,
    SplitIntoGuokui
}

public enum EnemyDecisionKind
{
    None,
    Move,
    Attack,
    HandledWithoutIntent
}

public enum EnemyDecisionStepStatus
{
    Failure,
    Success,
    DecisionReady
}

public readonly struct EnemyAggroTarget
{
    public readonly EntityHandle Handle;
    public readonly Vector2Int Position;
    public readonly EntityType EntityType;
    public readonly bool IsCoreBox;

    public EnemyAggroTarget(EntityHandle handle, Vector2Int position, EntityType entityType, bool isCoreBox)
    {
        Handle = handle;
        Position = position;
        EntityType = entityType;
        IsCoreBox = isCoreBox;
    }
}

public readonly struct EnemyProfile
{
    public readonly string DebugName;
    public readonly int MoveDistance;
    public readonly bool CanBreakWalls;
    public readonly bool UsesFutureAttackStandPoint;
    public readonly EnemyAttackPattern AttackPattern;
    public readonly EnemySpecialKind SpecialKind;
    public readonly int AttackRange;

    public EnemyProfile(
        string debugName,
        int moveDistance,
        bool canBreakWalls,
        bool usesFutureAttackStandPoint,
        EnemyAttackPattern attackPattern,
        EnemySpecialKind specialKind,
        int attackRange)
    {
        DebugName = debugName;
        MoveDistance = Mathf.Max(1, moveDistance);
        CanBreakWalls = canBreakWalls;
        UsesFutureAttackStandPoint = usesFutureAttackStandPoint;
        AttackPattern = attackPattern;
        SpecialKind = specialKind;
        AttackRange = Mathf.Max(1, attackRange);
    }
}

public sealed class EnemyProfileResolver
{
    private int _grenadierTagId = -1;
    private int _crossbowTagId = -1;
    private int _artilleryTagId = -1;
    private int _curseCasterTagId = -1;
    private int _guokuiTagId = -1;
    private int _ertongTagId = -1;

    public void ConfigureSpecialEnemyTags(int grenadierTagId, int crossbowTagId, int artilleryTagId)
    {
        _grenadierTagId = grenadierTagId;
        _crossbowTagId = crossbowTagId;
        _artilleryTagId = artilleryTagId;
    }

    public void ConfigureAdvancedEnemyTags(int curseCasterTagId, int guokuiTagId, int ertongTagId)
    {
        _curseCasterTagId = curseCasterTagId;
        _guokuiTagId = guokuiTagId;
        _ertongTagId = ertongTagId;
    }

    public EnemyProfile Resolve(in PropertyComponent props, int rangedAttackRange)
    {
        if (IsEnemyKind(props, _curseCasterTagId, "Curse", "Caster", "Sorcerer"))
        {
            return new EnemyProfile(
                "CurseCaster",
                1,
                false,
                false,
                EnemyAttackPattern.None,
                EnemySpecialKind.CurseLock,
                rangedAttackRange);
        }

        if (IsEnemyKind(props, _ertongTagId, "Ertong"))
        {
            return new EnemyProfile(
                "Ertong",
                4,
                true,
                false,
                EnemyAttackPattern.AdjacentSingleCell,
                EnemySpecialKind.SplitIntoGuokui,
                rangedAttackRange);
        }

        if (IsEnemyKind(props, _guokuiTagId, "Guokui"))
        {
            return new EnemyProfile(
                "Guokui",
                2,
                true,
                false,
                EnemyAttackPattern.AdjacentSingleCell,
                EnemySpecialKind.None,
                rangedAttackRange);
        }

        if (IsEnemyKind(props, _artilleryTagId, "Artillery", "Cannon"))
        {
            return new EnemyProfile(
                "Artillery",
                1,
                false,
                true,
                EnemyAttackPattern.LineSquare2x2RequiresWall,
                EnemySpecialKind.None,
                rangedAttackRange);
        }

        if (IsEnemyKind(props, _grenadierTagId, "Grenadier") ||
            IsEnemyKind(props, _crossbowTagId, "Crossbow", "Arbalest"))
        {
            return new EnemyProfile(
                "LineRanged",
                1,
                false,
                true,
                EnemyAttackPattern.LineSingleCellRequiresWall,
                EnemySpecialKind.None,
                rangedAttackRange);
        }

        return new EnemyProfile(
            "Melee",
            1,
            true,
            false,
            EnemyAttackPattern.AdjacentSingleCell,
            EnemySpecialKind.None,
            rangedAttackRange);
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
}

public struct EnemyAttackPayload
{
    public Vector2Int[] TargetPositions;
    public float[] DamageMultipliers;
    public int TargetCount;

    public static EnemyAttackPayload Single(Vector2Int targetPosition)
    {
        var payload = new EnemyAttackPayload();
        payload.AddTarget(targetPosition, 1f);
        return payload;
    }

    public void AddTarget(Vector2Int targetPosition, float damageMultiplier)
    {
        EnsureCapacity(TargetCount + 1);
        TargetPositions[TargetCount] = targetPosition;
        DamageMultipliers[TargetCount] = damageMultiplier;
        TargetCount++;
    }

    private void EnsureCapacity(int capacity)
    {
        if (TargetPositions != null && TargetPositions.Length >= capacity)
            return;

        int newCapacity = TargetPositions != null && TargetPositions.Length > 0 ? TargetPositions.Length : 4;
        while (newCapacity < capacity)
            newCapacity *= 2;

        if (TargetPositions == null)
        {
            TargetPositions = new Vector2Int[newCapacity];
            DamageMultipliers = new float[newCapacity];
            return;
        }

        System.Array.Resize(ref TargetPositions, newCapacity);
        System.Array.Resize(ref DamageMultipliers, newCapacity);
    }
}

public readonly struct EnemyDecision
{
    public readonly EnemyDecisionKind Kind;
    public readonly Vector2Int MoveDirection;
    public readonly int MoveDistance;
    public readonly EnemyAttackPayload AttackPayload;
    public readonly Vector2Int ReservedDestination;
    public readonly bool HasReservedDestination;
    public readonly string DebugLabel;

    private EnemyDecision(
        EnemyDecisionKind kind,
        Vector2Int moveDirection,
        int moveDistance,
        EnemyAttackPayload attackPayload,
        Vector2Int reservedDestination,
        bool hasReservedDestination,
        string debugLabel)
    {
        Kind = kind;
        MoveDirection = moveDirection;
        MoveDistance = moveDistance;
        AttackPayload = attackPayload;
        ReservedDestination = reservedDestination;
        HasReservedDestination = hasReservedDestination;
        DebugLabel = debugLabel;
    }

    public static EnemyDecision Move(Vector2Int direction, int distance, Vector2Int reservedDestination, string debugLabel)
    {
        return new EnemyDecision(
            EnemyDecisionKind.Move,
            direction,
            Mathf.Max(1, distance),
            default,
            reservedDestination,
            true,
            debugLabel);
    }

    public static EnemyDecision Attack(EnemyAttackPayload payload, string debugLabel)
    {
        return new EnemyDecision(
            EnemyDecisionKind.Attack,
            Vector2Int.zero,
            0,
            payload,
            Vector2Int.zero,
            false,
            debugLabel);
    }

    public static EnemyDecision Handled(string debugLabel)
    {
        return new EnemyDecision(
            EnemyDecisionKind.HandledWithoutIntent,
            Vector2Int.zero,
            0,
            default,
            Vector2Int.zero,
            false,
            debugLabel);
    }
}

public readonly struct EnemyDecisionContext
{
    public readonly EntitySystem EntitySystem;
    public readonly EntityHandle Actor;
    public readonly int EntityIndex;
    public readonly int EntityId;
    public readonly Vector2Int Position;
    public readonly PropertyComponent Properties;
    public readonly StatusComponent Status;
    public readonly EnemyAggroTarget AggroTarget;
    public readonly EnemyProfile Profile;
    public readonly int[] MovementField;

    public EnemyDecisionContext(
        EntitySystem entitySystem,
        EntityHandle actor,
        int entityIndex,
        Vector2Int position,
        PropertyComponent properties,
        StatusComponent status,
        EnemyAggroTarget aggroTarget,
        EnemyProfile profile,
        int[] movementField)
    {
        EntitySystem = entitySystem;
        Actor = actor;
        EntityIndex = entityIndex;
        EntityId = actor.Id;
        Position = position;
        Properties = properties;
        Status = status;
        AggroTarget = aggroTarget;
        Profile = profile;
        MovementField = movementField;
    }
}

public interface IEnemyDecisionStep
{
    EnemyDecisionStepStatus Evaluate(in EnemyDecisionContext context, out EnemyDecision decision);
}
