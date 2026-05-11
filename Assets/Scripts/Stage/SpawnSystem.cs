using UnityEngine;

/// <summary>
/// 刷怪系统。基于 CounterComponent 时间戳，为 Target.Enemy 实体周期性生成敌人。
/// 刷怪点被箱子压住时跳过，箱子移开后恢复。
/// </summary>
public class SpawnSystem : MonoBehaviour, ITickSystem
{
    public static SpawnSystem Instance { get; private set; }

    private EntityBP _defaultEnemyBP;
    private int _defaultEnemyTagId;
    private EnemySpawnDifficultyProfileSO _difficultyProfile;
    private float _overallDifficulty = 1f;
    private int _routeLayer;
    private int _routeLayerCount = 1;
    private bool _isActive;
    private bool _warnedMissingIntentSystem;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Initialize(EntityBP defaultEnemyBP, int defaultEnemyTagId = 0)
    {
        _defaultEnemyBP = defaultEnemyBP;
        _defaultEnemyTagId = defaultEnemyTagId;
    }

    public void ConfigureDifficultyProfile(EnemySpawnDifficultyProfileSO profile, float overallDifficulty, int routeLayer, int routeLayerCount)
    {
        _difficultyProfile = profile;
        _overallDifficulty = Mathf.Max(0f, overallDifficulty);
        _routeLayer = Mathf.Max(0, routeLayer);
        _routeLayerCount = Mathf.Max(1, routeLayerCount);
    }

    public void StartSpawning()
    {
        _isActive = true;
        Debug.Log("[SpawnSystem] Spawning started.");
    }

    public void StopSpawning()
    {
        _isActive = false;
    }

    public void Tick()
    {
        if (!_isActive)
            return;

        var entitySystem = EntitySystem.Instance;
        var intentSystem = IntentSystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized)
            return;

        if (intentSystem == null)
        {
            if (!_warnedMissingIntentSystem)
            {
                Debug.LogWarning("[SpawnSystem] IntentSystem is missing; spawn intents cannot be submitted.");
                _warnedMissingIntentSystem = true;
            }

            return;
        }

        int globalTick = entitySystem.GlobalTick;
        var entities = entitySystem.entities;

        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Target)
                continue;

            ref var counter = ref entities.counterComponents[i];
            if (counter.NextTick <= 0)
                continue;

            ref var props = ref entities.propertyComponents[i];
            var spawnPos = entities.coreComponents[i].Position;

            if (entitySystem.IsBlocked(spawnPos))
            {
                counter.NextTick++;
                Debug.Log($"[SpawnSystem] Spawn timer frozen at {spawnPos}, nextTick={counter.NextTick}.");
                continue;
            }

            if (counter.NextTick > globalTick)
                continue;

            // 时间戳到期
            var spawnIntent = intentSystem.Request<SpawnIntent>();
            spawnIntent.Setup(spawnPos, props.SpawnEntityBP);
            var actor = entitySystem.GetHandleFromId(entities.coreComponents[i].Id);
            Debug.Log($"[SpawnSystem] Submit spawn intent origin={spawnPos}, tick={globalTick}, bp={(props.SpawnEntityBP != null ? props.SpawnEntityBP.name : "<default>")}");
            if (!intentSystem.SetIntent(actor, IntentType.Spawn, spawnIntent))
            {
                Debug.LogWarning($"[SpawnSystem] Failed to set spawn intent for target id={entities.coreComponents[i].Id} at {spawnPos}.");
                intentSystem.Return(spawnIntent);
                continue;
            }
        }
    }

    public bool Execute(EntityHandle actor, SpawnIntent intent)
    {
        if (intent == null || !intent.Active)
            return false;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor))
            return false;

        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0)
            return false;

        if (IsCoveredByBox(entitySystem, intent.Origin))
        {
            Debug.Log($"[SpawnSystem] Spawn blocked: origin covered by box at {intent.Origin}.");
            return false;
        }

        if (entitySystem.IsBlocked(intent.Origin))
        {
            Debug.Log($"[SpawnSystem] Spawn blocked: origin is blocked at {intent.Origin}.");
            return false;
        }

        var handle = entitySystem.CreateEntity(EntityType.Enemy, intent.Origin);
        if (!entitySystem.IsValid(handle))
        {
            Debug.LogWarning($"[SpawnSystem] Spawn failed: EntitySystem refused enemy at {intent.Origin}.");
            return false;
        }

        ApplyBP(entitySystem, handle, intent.EntityBP);
        ref var props = ref entitySystem.entities.propertyComponents[actorIndex];
        ref var counter = ref entitySystem.entities.counterComponents[actorIndex];
        int interval = props.SpawnInterval > 0 ? props.SpawnInterval : 3;
        counter.NextTick = entitySystem.GlobalTick + interval;
        Debug.Log($"[SpawnSystem] Spawned enemy at {intent.Origin}.");
        return true;
    }

    public bool SpawnImmediatelyFromTarget(EntityHandle actor)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(actor))
            return false;

        int actorIndex = entitySystem.GetIndex(actor);
        if (actorIndex < 0 || entitySystem.entities.coreComponents[actorIndex].EntityType != EntityType.Target)
            return false;

        var origin = entitySystem.entities.coreComponents[actorIndex].Position;
        if (IsCoveredByBox(entitySystem, origin) || entitySystem.IsBlocked(origin))
            return false;

        var handle = entitySystem.CreateEntity(EntityType.Enemy, origin);
        if (!entitySystem.IsValid(handle))
            return false;

        ref var props = ref entitySystem.entities.propertyComponents[actorIndex];
        ApplyBP(entitySystem, handle, props.SpawnEntityBP);

        ref var counter = ref entitySystem.entities.counterComponents[actorIndex];
        int interval = props.SpawnInterval > 0 ? props.SpawnInterval : 3;
        counter.NextTick = entitySystem.GlobalTick + interval;
        Debug.Log($"[SpawnSystem] Immediately spawned enemy at {origin}.");
        return true;
    }

    private static bool IsCoveredByBox(EntitySystem entitySystem, Vector2Int cell)
    {
        var occupant = entitySystem.GetOccupant(cell);
        if (!entitySystem.IsValid(occupant))
            return false;

        int index = entitySystem.GetIndex(occupant);
        return index >= 0 &&
               entitySystem.entities.coreComponents[index].EntityType == EntityType.Box;
    }

    private void ApplyBP(EntitySystem entitySystem, EntityHandle handle, EntityBP spawnEntityBP)
    {
        int index = entitySystem.GetIndex(handle);
        if (index < 0)
            return;

        ref var properties = ref entitySystem.entities.propertyComponents[index];
        ref var core = ref entitySystem.entities.coreComponents[index];
        EntityBP bp = ResolveSpawnBP(entitySystem, spawnEntityBP, core.Position);
        if (bp == null)
            return;

        ref var status = ref entitySystem.entities.statusComponents[index];
        status.BaseMaxHealth = Mathf.Max(1, bp.health);
        status.BaseAttack = Mathf.Max(0, bp.attack);
        status.DamageTaken = 0;
        status.AttackModifier = 0;
        status.MaxHealthModifier = 0;
        properties.Attack = CombatStats.GetAttack(status);
        properties.SourceTagId = _defaultEnemyTagId;
        properties.SourceBP = bp;

        entitySystem.PublishEntityCreated(handle);
    }

    private EntityBP ResolveSpawnBP(EntitySystem entitySystem, EntityBP spawnEntityBP, Vector2Int spawnPosition)
    {
        EntityBP fallback = spawnEntityBP != null ? spawnEntityBP : _defaultEnemyBP;

        if (_difficultyProfile == null)
            return fallback;

        int tick = entitySystem != null ? entitySystem.GlobalTick : 0;
        return _difficultyProfile.Roll(
            _overallDifficulty,
            _routeLayer,
            _routeLayerCount,
            tick,
            spawnPosition,
            fallback);
    }

}
