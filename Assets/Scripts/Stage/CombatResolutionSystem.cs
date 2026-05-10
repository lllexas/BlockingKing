using UnityEngine;

public class CombatResolutionSystem : MonoBehaviour
{
    public static CombatResolutionSystem Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        EventBusSystem.Instance?.On(StageEventType.DeathCheck, OnDeathCheck);
    }

    private void OnDisable()
    {
        EventBusSystem.Instance?.Off(StageEventType.DeathCheck, OnDeathCheck);
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

            if (CombatStats.GetCurrentHealth(entities.statusComponents[i]) > 0)
                continue;

            var handle = entitySystem.GetHandleFromId(core.Id);
            entitySystem.DestroyEntity(handle);
            context.AnyDeathResolved = true;
        }
    }

    private static bool CanDie(EntityType entityType)
    {
        return entityType == EntityType.Enemy ||
               entityType == EntityType.Wall ||
               entityType == EntityType.Box ||
               entityType == EntityType.Player;
    }
}
