using UnityEngine;

public sealed class BoxBlockSystem : MonoBehaviour
{
    public static BoxBlockSystem Instance { get; private set; }

    [SerializeField, Min(0)] private int blockPerPushedCell = 1;
    [SerializeField, Min(0)] private int fullBlockIdleTurns = 1;
    [SerializeField] private bool applyToRewardBoxes;

    private readonly System.Collections.Generic.HashSet<int> _movedBoxIdsThisTick = new();
    private int _lastResolvedTick = -1;

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        EventBusSystem.Instance?.On(StageEventType.EntityMoved, OnEntityMoved, -10);
        EventBusSystem.Instance?.On(StageEventType.IntentResolutionEnd, OnIntentResolutionEnd, -100);
    }

    private void OnDisable()
    {
        EventBusSystem.Instance?.Off(StageEventType.EntityMoved, OnEntityMoved);
        EventBusSystem.Instance?.Off(StageEventType.IntentResolutionEnd, OnIntentResolutionEnd);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEntityMoved(StageEvent evt)
    {
        if (evt.EntityType != EntityType.Box)
            return;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(evt.Entity))
            return;

        int index = entitySystem.GetIndex(evt.Entity);
        if (index < 0 || !ShouldApplyToBox(entitySystem, index))
            return;

        int pushedCells = Mathf.Max(1, Mathf.Abs(evt.To.x - evt.From.x) + Mathf.Abs(evt.To.y - evt.From.y));
        ref var status = ref entitySystem.entities.statusComponents[index];
        status.Block = Mathf.Max(0, status.Block) + pushedCells * blockPerPushedCell;
        status.BlockIdleTurns = 0;
        _movedBoxIdsThisTick.Add(evt.Entity.Id);
    }

    private void OnIntentResolutionEnd(StageEvent evt)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        int globalTick = entitySystem.GlobalTick;
        if (_lastResolvedTick == globalTick)
            return;

        _lastResolvedTick = globalTick;
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Box || !ShouldApplyToBox(entitySystem, i))
                continue;

            ref var status = ref entities.statusComponents[i];
            int boxId = entities.coreComponents[i].Id;
            if (_movedBoxIdsThisTick.Contains(boxId))
            {
                status.BlockIdleTurns = 0;
                continue;
            }

            if (status.Block <= 0)
            {
                status.BlockIdleTurns = 0;
                continue;
            }

            status.BlockIdleTurns++;
            if (status.BlockIdleTurns <= fullBlockIdleTurns)
                continue;

            int decayStep = status.BlockIdleTurns - fullBlockIdleTurns;
            status.Block = decayStep <= 1 ? Mathf.FloorToInt(status.Block * 0.5f) : 0;

            if (status.Block <= 0)
            {
                status.Block = 0;
                status.BlockIdleTurns = 0;
            }
        }

        _movedBoxIdsThisTick.Clear();
    }

    private bool ShouldApplyToBox(EntitySystem entitySystem, int index)
    {
        return applyToRewardBoxes || entitySystem.entities.propertyComponents[index].IsCore;
    }
}
