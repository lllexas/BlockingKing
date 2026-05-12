using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Intent 对象池 + 消费入口。
/// </summary>
public enum EnemyIntentPresentationMode
{
    AllInOneBatch,
    BatchByIntentType,
    Serial
}

public enum EnemyBeatKind : byte
{
    None,
    Empty,
    Move,
    Spawn,
    Attack
}

public class IntentSystem : MonoBehaviour
{
    public static IntentSystem Instance { get; private set; }
    public event Action IntentQueueCompleted;

    [SerializeField] private EnemyIntentPresentationMode enemyIntentPresentationMode = EnemyIntentPresentationMode.AllInOneBatch;

    private readonly Dictionary<Type, Stack<Intent>> _pools = new();
    private readonly List<EntityHandle> _activeIntentEntities = new();
    private readonly List<IntentExecutionStep> _executionSteps = new();
    private readonly List<EntityHandle> _enemyIntentBatch = new();
    private readonly List<EntityHandle> _enemyMoveBatch = new();
    private readonly List<EntityHandle> _enemyAttackBatch = new();
    private readonly List<EntityHandle> _allInOneBatch = new();
    private readonly List<EntityHandle> _spawnBatch = new();
    private EnemyBeatKind _allInOneBeatKind = EnemyBeatKind.None;
    private EnemyBeatKind _spawnBeatKind = EnemyBeatKind.None;
    private EntityHandle _playerIntentActor = EntityHandle.None;
    private Coroutine _runner;
    private const int MaxResolutionPasses = 8;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool IsRunning => _runner != null;
    public EnemyIntentPresentationMode EnemyPresentationMode => enemyIntentPresentationMode;
    public bool CanAcceptTick => _runner == null;

    public void ConfigureEnemyIntentPresentation(EnemyIntentPresentationMode mode)
    {
        enemyIntentPresentationMode = mode;
    }

    [System.Obsolete("Use ConfigureEnemyIntentPresentation.")]
    public void ConfigureEnemyIntentBatching(bool enabled)
    {
        ConfigureEnemyIntentPresentation(enabled
            ? EnemyIntentPresentationMode.AllInOneBatch
            : EnemyIntentPresentationMode.Serial);
    }

    public bool Tick()
    {
        if (_runner != null)
            return false;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        BuildExecutionSteps(entitySystem);

        _activeIntentEntities.Clear();
        _playerIntentActor = EntityHandle.None;

        if (_executionSteps.Count == 0)
            return false;

        Debug.Log($"[IntentSystem] Run intent queue: steps={_executionSteps.Count}, enemyBatch={_enemyIntentBatch.Count}, enemyMoveBatch={_enemyMoveBatch.Count}, enemyAttackBatch={_enemyAttackBatch.Count}, spawnBatch={_spawnBatch.Count}, enemyMode={enemyIntentPresentationMode}");
        _runner = StartCoroutine(RunIntentQueue());
        return true;
    }

    // ──────── 对象池 ────────

    public T Request<T>() where T : Intent, new()
    {
        var type = typeof(T);
        if (!_pools.TryGetValue(type, out var stack))
        {
            stack = new Stack<Intent>();
            _pools[type] = stack;
        }

        if (stack.Count > 0)
            return (T)stack.Pop();

        return new T();
    }

    public void Return(Intent intent)
    {
        if (intent == null) return;
        intent.Reset();

        var type = intent.GetType();
        if (!_pools.TryGetValue(type, out var stack))
        {
            stack = new Stack<Intent>();
            _pools[type] = stack;
        }
        stack.Push(intent);
    }

    public bool SetIntent(EntityHandle actor, IntentType intentType, Intent intent)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsValid(actor) || intent == null)
            return false;

        int index = entitySystem.GetIndex(actor);
        if (index < 0)
            return false;

        ref var intentComponent = ref entitySystem.entities.intentComponents[index];
        if (intentComponent.Intent != null)
            Return(intentComponent.Intent);

        intentComponent.Type = intentType;
        intentComponent.Intent = intent;

        if (!_activeIntentEntities.Contains(actor))
            _activeIntentEntities.Add(actor);

        return true;
    }

    public bool SetPlayerIntent(EntityHandle actor, IntentType intentType, Intent intent)
    {
        if (!SetIntent(actor, intentType, intent))
            return false;

        _playerIntentActor = actor;
        return true;
    }

    private IEnumerator RunIntentQueue()
    {
        for (int i = 0; i < _executionSteps.Count; i++)
        {
            var entitySystem = EntitySystem.Instance;
            if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
                break;

            var step = _executionSteps[i];
            if (step.IsBatch)
                yield return ExecuteBatchRoutine(entitySystem, step.Actors, step.EnemyBeatKind);
            else
                yield return ExecuteIntentRoutine(entitySystem, step.Actor, true);
        }

        _executionSteps.Clear();
        _runner = null;
        IntentQueueCompleted?.Invoke();
    }

    private void BuildExecutionSteps(EntitySystem entitySystem)
    {
        _executionSteps.Clear();
        _allInOneBatch.Clear();
        _enemyIntentBatch.Clear();
        _enemyMoveBatch.Clear();
        _enemyAttackBatch.Clear();
        _spawnBatch.Clear();
        _allInOneBeatKind = EnemyBeatKind.None;
        _spawnBeatKind = EnemyBeatKind.None;

        _playerIntentActor = ResolvePlayerIntentActor(entitySystem);
        if (_playerIntentActor != EntityHandle.None)
            _executionSteps.Add(IntentExecutionStep.Single(_playerIntentActor));

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var intent = ref entities.intentComponents[i];
            if (intent.Type == IntentType.None || intent.Intent == null)
                continue;

            var actor = entitySystem.GetHandleFromId(entities.coreComponents[i].Id);
            if (actor == _playerIntentActor)
                continue;

            if (enemyIntentPresentationMode == EnemyIntentPresentationMode.AllInOneBatch &&
                CanBatchAllInOneIntent(entitySystem, i))
            {
                _allInOneBatch.Add(actor);
                if (CanBatchEnemyIntent(entitySystem, i))
                    _enemyIntentBatch.Add(actor);
                else if (CanBatchSpawn(entitySystem, i))
                    _spawnBatch.Add(actor);

                _allInOneBeatKind = MergeEnemyBeatKind(_allInOneBeatKind, ResolveEnemyBeatKind(entitySystem, i));
                continue;
            }

            if (enemyIntentPresentationMode == EnemyIntentPresentationMode.BatchByIntentType &&
                TryGetTypedEnemyIntentBatch(entitySystem, i, out var typedEnemyBatch))
            {
                typedEnemyBatch.Add(actor);
                continue;
            }

            if (CanBatchSpawn(entitySystem, i))
            {
                _spawnBatch.Add(actor);
                _spawnBeatKind = MergeEnemyBeatKind(_spawnBeatKind, EnemyBeatKind.Spawn);
                continue;
            }

            _executionSteps.Add(IntentExecutionStep.Single(actor));
        }

        if (enemyIntentPresentationMode == EnemyIntentPresentationMode.AllInOneBatch)
            _executionSteps.Insert(_playerIntentActor != EntityHandle.None ? 1 : 0, IntentExecutionStep.Batch(_allInOneBatch, ResolveAllInOneBeatKind()));

        if (enemyIntentPresentationMode != EnemyIntentPresentationMode.AllInOneBatch && _allInOneBatch.Count > 0)
            _executionSteps.Insert(_playerIntentActor != EntityHandle.None ? 1 : 0, IntentExecutionStep.Batch(_allInOneBatch, ResolveAllInOneBeatKind()));

        if (enemyIntentPresentationMode != EnemyIntentPresentationMode.AllInOneBatch && _spawnBatch.Count > 0)
            _executionSteps.Insert(_playerIntentActor != EntityHandle.None ? 1 : 0, IntentExecutionStep.Batch(_spawnBatch, _spawnBeatKind));

        if (enemyIntentPresentationMode == EnemyIntentPresentationMode.AllInOneBatch)
            return;

        if (_enemyIntentBatch.Count > 0)
            _executionSteps.Insert(_playerIntentActor != EntityHandle.None ? 1 + (_spawnBatch.Count > 0 ? 1 : 0) : (_spawnBatch.Count > 0 ? 1 : 0), IntentExecutionStep.Batch(_enemyIntentBatch, ResolveBatchBeatKind(entitySystem, _enemyIntentBatch)));

        if (enemyIntentPresentationMode == EnemyIntentPresentationMode.BatchByIntentType)
        {
            int insertIndex = _playerIntentActor != EntityHandle.None ? 1 + (_spawnBatch.Count > 0 ? 1 : 0) : (_spawnBatch.Count > 0 ? 1 : 0);
            if (_enemyMoveBatch.Count > 0)
                _executionSteps.Insert(insertIndex++, IntentExecutionStep.Batch(_enemyMoveBatch, EnemyBeatKind.Move));

            if (_enemyAttackBatch.Count > 0)
                _executionSteps.Insert(insertIndex, IntentExecutionStep.Batch(_enemyAttackBatch, EnemyBeatKind.Attack));
        }
    }

    private EnemyBeatKind ResolveAllInOneBeatKind()
    {
        return _allInOneBeatKind == EnemyBeatKind.None ? EnemyBeatKind.Empty : _allInOneBeatKind;
    }

    private EnemyBeatKind ResolveBatchBeatKind(EntitySystem entitySystem, List<EntityHandle> actors)
    {
        EnemyBeatKind result = EnemyBeatKind.None;
        for (int i = 0; i < actors.Count; i++)
        {
            int index = entitySystem.GetIndex(actors[i]);
            result = MergeEnemyBeatKind(result, ResolveEnemyBeatKind(entitySystem, index));
        }

        return result == EnemyBeatKind.None ? EnemyBeatKind.Empty : result;
    }

    private static EnemyBeatKind ResolveEnemyBeatKind(EntitySystem entitySystem, int index)
    {
        if (entitySystem == null || index < 0)
            return EnemyBeatKind.None;

        ref var intent = ref entitySystem.entities.intentComponents[index];
        return intent.Type switch
        {
            IntentType.Spawn => EnemyBeatKind.Spawn,
            IntentType.Move => EnemyBeatKind.Move,
            _ => EnemyBeatKind.None
        };
    }

    private static EnemyBeatKind MergeEnemyBeatKind(EnemyBeatKind current, EnemyBeatKind next)
    {
        return (EnemyBeatKind)Mathf.Max((int)current, (int)next);
    }

    private EntityHandle ResolvePlayerIntentActor(EntitySystem entitySystem)
    {
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return EntityHandle.None;

        if (entitySystem.IsValid(_playerIntentActor))
            return _playerIntentActor;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            if (entities.intentComponents[i].Type == IntentType.None || entities.intentComponents[i].Intent == null)
                continue;

            return entitySystem.GetHandleFromId(entities.coreComponents[i].Id);
        }

        return EntityHandle.None;
    }

    private static bool CanBatchEnemyIntent(EntitySystem entitySystem, int index)
    {
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null || index < 0)
            return false;

        if (entitySystem.entities.coreComponents[index].EntityType != EntityType.Enemy)
            return false;

        ref var intent = ref entitySystem.entities.intentComponents[index];
        return (intent.Type == IntentType.Move && intent.Intent is MoveIntent) ||
               (intent.Type == IntentType.Attack && intent.Intent is AttackIntent);
    }

    private static bool CanBatchAllInOneIntent(EntitySystem entitySystem, int index)
    {
        return CanBatchEnemyIntent(entitySystem, index) || CanBatchSpawn(entitySystem, index);
    }

    private bool TryGetTypedEnemyIntentBatch(EntitySystem entitySystem, int index, out List<EntityHandle> batch)
    {
        batch = null;
        if (!CanBatchEnemyIntent(entitySystem, index))
            return false;

        ref var intent = ref entitySystem.entities.intentComponents[index];
        if (intent.Type == IntentType.Move)
        {
            batch = _enemyMoveBatch;
            return true;
        }

        if (intent.Type == IntentType.Attack)
        {
            batch = _enemyAttackBatch;
            return true;
        }

        return false;
    }

    private static bool CanBatchSpawn(EntitySystem entitySystem, int index)
    {
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null || index < 0)
            return false;

        return entitySystem.entities.intentComponents[index].Type == IntentType.Spawn
               && entitySystem.entities.intentComponents[index].Intent is SpawnIntent;
    }

    private IEnumerator ExecuteBatchRoutine(EntitySystem entitySystem, List<EntityHandle> actors, EnemyBeatKind enemyBeatKind)
    {
        if (actors == null || actors.Count == 0)
        {
            EventBusSystem.Instance?.Publish(new StageEvent(
                StageEventType.PresentationBeat,
                enemyBeatKind: enemyBeatKind == EnemyBeatKind.None ? EnemyBeatKind.Empty : enemyBeatKind));
            yield return WaitForPresentation();
            yield break;
        }

        var eventBus = EventBusSystem.Instance;
        eventBus?.Publish(new StageEvent(
            StageEventType.PresentationBatchBegin,
            enemyBeatKind: enemyBeatKind == EnemyBeatKind.None ? EnemyBeatKind.Empty : enemyBeatKind));

        for (int i = 0; i < actors.Count; i++)
        {
            entitySystem = EntitySystem.Instance;
            if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
                break;

            yield return ExecuteIntentRoutine(entitySystem, actors[i], false);
        }

        eventBus?.Publish(new StageEvent(StageEventType.PresentationBatchEnd));
        yield return WaitForPresentation();
    }

    private IEnumerator ExecuteIntentRoutine(EntitySystem entitySystem, EntityHandle actor, bool waitForPresentation)
    {
        if (entitySystem == null || !entitySystem.IsValid(actor))
            yield break;

        int entityIndex = entitySystem.GetIndex(actor);
        if (entityIndex < 0)
            yield break;

        IntentType executingType = entitySystem.entities.intentComponents[entityIndex].Type;
        Intent executingIntent = entitySystem.entities.intentComponents[entityIndex].Intent;
        if (executingType == IntentType.None || executingIntent == null)
            yield break;

        entitySystem.entities.intentComponents[entityIndex].Type = IntentType.None;
        entitySystem.entities.intentComponents[entityIndex].Intent = null;

        var eventBus = EventBusSystem.Instance;
        eventBus?.Publish(new StageEvent(
            StageEventType.BeforeIntentExecute,
            actor: actor,
            entity: actor,
            entityType: entitySystem.entities.coreComponents[entityIndex].EntityType,
            intentType: executingType,
            intent: executingIntent,
            from: entitySystem.entities.coreComponents[entityIndex].Position,
            to: entitySystem.entities.coreComponents[entityIndex].Position,
            sourceTagId: entitySystem.entities.propertyComponents[entityIndex].SourceTagId));

        switch (executingType)
        {
            case IntentType.Noop:
                break;
            case IntentType.Move:
                MoveSystem.Instance?.Execute(actor, executingIntent as MoveIntent);
                break;
            case IntentType.Attack:
                AttackSystem.Instance?.Execute(actor, executingIntent as AttackIntent);
                break;
            case IntentType.Card:
                if (CardEffectSystem.Instance != null)
                    CardEffectSystem.Instance.Execute(actor, executingIntent as CardIntent);
                else
                    Debug.LogWarning("[IntentSystem] Card intent was submitted, but CardEffectSystem is missing.");
                break;
            case IntentType.Spawn:
                if (SpawnSystem.Instance != null)
                    SpawnSystem.Instance.Execute(actor, executingIntent as SpawnIntent);
                else
                    Debug.LogWarning("[IntentSystem] Spawn intent was submitted, but SpawnSystem is missing.");
                break;
        }

        if (entitySystem.IsValid(actor))
        {
            int postIndex = entitySystem.GetIndex(actor);
            eventBus?.Publish(new StageEvent(
                StageEventType.AfterIntentExecute,
                actor: actor,
                entity: actor,
                entityType: entitySystem.entities.coreComponents[postIndex].EntityType,
                intentType: executingType,
                intent: executingIntent,
                from: entitySystem.entities.coreComponents[postIndex].Position,
                to: entitySystem.entities.coreComponents[postIndex].Position,
                sourceTagId: entitySystem.entities.propertyComponents[postIndex].SourceTagId));
        }
        else
        {
            eventBus?.Publish(new StageEvent(
                StageEventType.AfterIntentExecute,
                actor: actor,
                entity: actor,
                intentType: executingType,
                intent: executingIntent));
        }

        ResolveWorldState(actor, executingType);

        Return(executingIntent);

        if (waitForPresentation)
            yield return WaitForPresentation();
    }

    private static IEnumerator WaitForPresentation()
    {
        var drawSystem = DrawSystem.Instance;
        while (drawSystem != null && drawSystem.IsBeatMotionBusy)
        {
            yield return null;
            drawSystem = DrawSystem.Instance;
        }
    }

    private readonly struct IntentExecutionStep
    {
        public readonly bool IsBatch;
        public readonly EntityHandle Actor;
        public readonly List<EntityHandle> Actors;
        public readonly EnemyBeatKind EnemyBeatKind;

        private IntentExecutionStep(EntityHandle actor)
        {
            IsBatch = false;
            Actor = actor;
            Actors = null;
            EnemyBeatKind = EnemyBeatKind.None;
        }

        private IntentExecutionStep(List<EntityHandle> actors, EnemyBeatKind enemyBeatKind)
        {
            IsBatch = true;
            Actor = EntityHandle.None;
            Actors = new List<EntityHandle>(actors);
            EnemyBeatKind = enemyBeatKind;
        }

        public static IntentExecutionStep Single(EntityHandle actor)
        {
            return new IntentExecutionStep(actor);
        }

        public static IntentExecutionStep Batch(List<EntityHandle> actors, EnemyBeatKind enemyBeatKind)
        {
            return new IntentExecutionStep(actors, enemyBeatKind);
        }
    }

    public void ResolveWorldState()
    {
        ResolveWorldState(EntityHandle.None, IntentType.None);
    }

    public void ResolveWorldState(EntityHandle actor, IntentType intentType)
    {
        var eventBus = EventBusSystem.Instance;
        if (eventBus == null)
            return;

        var context = new IntentResolutionContext
        {
            Actor = actor,
            IntentType = intentType
        };

        eventBus.Publish(new StageEvent(
            StageEventType.IntentResolutionBegin,
            actor: actor,
            intentType: intentType,
            resolutionContext: context));

        for (int pass = 0; pass < MaxResolutionPasses; pass++)
        {
            context.Pass = pass;
            context.AnyDeathResolved = false;

            eventBus.Publish(new StageEvent(
                StageEventType.AuraUpdate,
                actor: actor,
                intentType: intentType,
                resolutionContext: context));

            eventBus.Publish(new StageEvent(
                StageEventType.DeathCheck,
                actor: actor,
                intentType: intentType,
                resolutionContext: context));

            if (!context.AnyDeathResolved)
                break;
        }

        eventBus.Publish(new StageEvent(
            StageEventType.IntentResolutionEnd,
            actor: actor,
            intentType: intentType,
            resolutionContext: context));
    }

    public void ForEachActiveIntent(System.Action<EntityHandle, IntentComponent> visitor)
    {
        if (visitor == null)
            return;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        for (int i = 0; i < _activeIntentEntities.Count; i++)
        {
            var actor = _activeIntentEntities[i];
            int index = entitySystem.GetIndex(actor);
            if (index < 0)
                continue;

            var intentComponent = entitySystem.entities.intentComponents[index];
            if (intentComponent.Type == IntentType.None || intentComponent.Intent == null)
                continue;

            visitor(actor, intentComponent);
        }
    }

    public void Clear()
    {
        foreach (var stack in _pools.Values)
            stack.Clear();
        _pools.Clear();
        _activeIntentEntities.Clear();
        _executionSteps.Clear();
        _allInOneBatch.Clear();
        _enemyIntentBatch.Clear();
        _enemyMoveBatch.Clear();
        _enemyAttackBatch.Clear();
        _spawnBatch.Clear();
        _playerIntentActor = EntityHandle.None;
        if (_runner != null)
        {
            StopCoroutine(_runner);
            _runner = null;
        }
    }
}
