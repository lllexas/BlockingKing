using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Intent 对象池 + 消费入口。
/// </summary>
public class IntentSystem : MonoBehaviour
{
    public static IntentSystem Instance { get; private set; }

    private readonly Dictionary<Type, Stack<Intent>> _pools = new();
    private readonly List<EntityHandle> _activeIntentEntities = new();
    private readonly List<IntentExecutionStep> _executionSteps = new();
    private readonly List<EntityHandle> _enemyMoveBatch = new();
    private readonly List<EntityHandle> _spawnBatch = new();
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

    public void Tick()
    {
        if (_runner != null)
            return;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        BuildExecutionSteps(entitySystem);

        _activeIntentEntities.Clear();
        _playerIntentActor = EntityHandle.None;

        if (_executionSteps.Count == 0)
            return;

        _runner = StartCoroutine(RunIntentQueue());
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
                yield return ExecuteBatchRoutine(entitySystem, step.Actors);
            else
                yield return ExecuteIntentRoutine(entitySystem, step.Actor, true);
        }

        _executionSteps.Clear();
        _runner = null;
    }

    private void BuildExecutionSteps(EntitySystem entitySystem)
    {
        _executionSteps.Clear();
        _enemyMoveBatch.Clear();
        _spawnBatch.Clear();

        if (_playerIntentActor != EntityHandle.None)
            _executionSteps.Add(IntentExecutionStep.Single(_playerIntentActor));

        for (int i = 0; i < _activeIntentEntities.Count; i++)
        {
            var actor = _activeIntentEntities[i];
            if (actor == _playerIntentActor)
                continue;

            if (CanBatchEnemyMove(entitySystem, actor))
            {
                _enemyMoveBatch.Add(actor);
                continue;
            }

            if (CanBatchSpawn(entitySystem, actor))
            {
                _spawnBatch.Add(actor);
                continue;
            }

            _executionSteps.Add(IntentExecutionStep.Single(actor));
        }

        if (_spawnBatch.Count > 0)
            _executionSteps.Insert(_playerIntentActor != EntityHandle.None ? 1 : 0, IntentExecutionStep.Batch(_spawnBatch));

        if (_enemyMoveBatch.Count > 0)
            _executionSteps.Insert(_playerIntentActor != EntityHandle.None ? 1 + (_spawnBatch.Count > 0 ? 1 : 0) : (_spawnBatch.Count > 0 ? 1 : 0), IntentExecutionStep.Batch(_enemyMoveBatch));
    }

    private static bool CanBatchEnemyMove(EntitySystem entitySystem, EntityHandle actor)
    {
        if (entitySystem == null || !entitySystem.IsValid(actor))
            return false;

        int index = entitySystem.GetIndex(actor);
        if (index < 0)
            return false;

        return entitySystem.entities.coreComponents[index].EntityType == EntityType.Enemy
               && entitySystem.entities.intentComponents[index].Type == IntentType.Move
               && entitySystem.entities.intentComponents[index].Intent is MoveIntent;
    }

    private static bool CanBatchSpawn(EntitySystem entitySystem, EntityHandle actor)
    {
        if (entitySystem == null || !entitySystem.IsValid(actor))
            return false;

        int index = entitySystem.GetIndex(actor);
        if (index < 0)
            return false;

        return entitySystem.entities.intentComponents[index].Type == IntentType.Spawn
               && entitySystem.entities.intentComponents[index].Intent is SpawnIntent;
    }

    private IEnumerator ExecuteBatchRoutine(EntitySystem entitySystem, List<EntityHandle> actors)
    {
        var eventBus = EventBusSystem.Instance;
        eventBus?.Publish(new StageEvent(StageEventType.PresentationBatchBegin));

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

        if (waitForPresentation && ShouldExecuteAfterPrePresentation(executingType))
            yield return WaitForPresentation();

        switch (executingType)
        {
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

        if (waitForPresentation && !ShouldExecuteAfterPrePresentation(executingType))
            yield return WaitForPresentation();
    }

    private static bool ShouldExecuteAfterPrePresentation(IntentType intentType)
    {
        return intentType == IntentType.Attack;
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

        private IntentExecutionStep(EntityHandle actor)
        {
            IsBatch = false;
            Actor = actor;
            Actors = null;
        }

        private IntentExecutionStep(List<EntityHandle> actors)
        {
            IsBatch = true;
            Actor = EntityHandle.None;
            Actors = new List<EntityHandle>(actors);
        }

        public static IntentExecutionStep Single(EntityHandle actor)
        {
            return new IntentExecutionStep(actor);
        }

        public static IntentExecutionStep Batch(List<EntityHandle> actors)
        {
            return new IntentExecutionStep(actors);
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
        _enemyMoveBatch.Clear();
        _spawnBatch.Clear();
        _playerIntentActor = EntityHandle.None;
        if (_runner != null)
        {
            StopCoroutine(_runner);
            _runner = null;
        }
    }
}
