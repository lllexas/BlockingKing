using UnityEngine;
using System.Collections.Generic;

public class CombatResolutionSystem : MonoBehaviour
{
    public static CombatResolutionSystem Instance { get; private set; }
    private readonly Dictionary<int, DamageSourceSnapshot> _lastDamageByEntityId = new();

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        EventBusSystem.Instance?.On(StageEventType.EntityDamaged, OnEntityDamaged);
        EventBusSystem.Instance?.On(StageEventType.DeathCheck, OnDeathCheck);
        EventBusSystem.Instance?.On(StageEventType.IntentResolutionEnd, OnIntentResolutionEnd);
    }

    private void OnDisable()
    {
        EventBusSystem.Instance?.Off(StageEventType.EntityDamaged, OnEntityDamaged);
        EventBusSystem.Instance?.Off(StageEventType.DeathCheck, OnDeathCheck);
        EventBusSystem.Instance?.Off(StageEventType.IntentResolutionEnd, OnIntentResolutionEnd);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnDeathCheck(StageEvent evt)
    {
        var context = evt.ResolutionContext;
        var entitySystem = EntitySystem.Instance;
        if (context == null || entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        var entities = entitySystem.entities;
        for (int i = entities.entityCount - 1; i >= 0; i--)
        {
            ref var core = ref entities.coreComponents[i];
            if (!CanDie(core.EntityType))
                continue;

            int currentHealth = CombatStats.GetCurrentHealth(entities.statusComponents[i]);
            if (currentHealth > 0)
                continue;

            var handle = entitySystem.GetHandleFromId(core.Id);
            Vector2Int sourcePosition = default;
            bool hasSource = TryGetDamageSource(handle, out sourcePosition);
            entitySystem.DestroyEntity(handle, currentHealth, sourcePosition, hasSource);
            context.AnyDeathResolved = true;
        }
    }

    private void OnEntityDamaged(StageEvent evt)
    {
        if (evt.Entity.Id < 0 || !evt.HasSourcePosition)
            return;

        _lastDamageByEntityId[evt.Entity.Id] = new DamageSourceSnapshot(evt.Entity.Version, evt.SourcePosition);
    }

    private void OnIntentResolutionEnd(StageEvent evt)
    {
        _lastDamageByEntityId.Clear();
    }

    private bool TryGetDamageSource(EntityHandle entity, out Vector2Int sourcePosition)
    {
        sourcePosition = default;
        if (entity.Id < 0 ||
            !_lastDamageByEntityId.TryGetValue(entity.Id, out var snapshot) ||
            snapshot.Version != entity.Version)
        {
            return false;
        }

        sourcePosition = snapshot.SourcePosition;
        return true;
    }

    private static bool CanDie(EntityType entityType)
    {
        return entityType == EntityType.Enemy ||
               entityType == EntityType.Wall ||
               entityType == EntityType.Box ||
               entityType == EntityType.Player;
    }

    private readonly struct DamageSourceSnapshot
    {
        public readonly int Version;
        public readonly Vector2Int SourcePosition;

        public DamageSourceSnapshot(int version, Vector2Int sourcePosition)
        {
            Version = version;
            SourcePosition = sourcePosition;
        }
    }
}
