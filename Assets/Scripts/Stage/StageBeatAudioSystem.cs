using UnityEngine;

public class StageBeatAudioSystem : MonoBehaviour
{
    [Header("Player Beat")]
    [SerializeField] private AudioClip playerMoveBeat;
    [SerializeField] private AudioClip playerPushBoxBeat;
    [SerializeField] private AudioClip playerCardBeat;
    [SerializeField] private AudioClip playerNoopBeat;

    [Header("Enemy Beat")]
    [SerializeField] private AudioClip enemyHitBeat;
    [SerializeField] private AudioClip enemySpawnBeat;
    [SerializeField] private AudioClip enemyMoveBeat;
    [SerializeField] private AudioClip enemyEmptyBeat;

    [Header("Playback")]
    [SerializeField, Range(0f, 1f)] private float volumeScale = 1f;
    [SerializeField] private bool logBeatAudio;
    [SerializeField] private bool testPlayerMoveBeatOnStart;

    private EventBusSystem _registeredBus;

    private void OnEnable()
    {
        TryRegister();
    }

    private void Start()
    {
        TryRegister();

        if (testPlayerMoveBeatOnStart)
            Play(playerMoveBeat, "test player move");
    }

    private void Update()
    {
        if (_registeredBus == null)
            TryRegister();
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void TryRegister()
    {
        var bus = EventBusSystem.Instance;
        if (bus == null || _registeredBus == bus)
            return;

        Unregister();
        _registeredBus = bus;
        _registeredBus.On(StageEventType.BeforeIntentExecute, OnStageEvent, -20);
        _registeredBus.On(StageEventType.EntityDamaged, OnStageEvent, -20);
        _registeredBus.On(StageEventType.PresentationBatchBegin, OnStageEvent, -20);
        _registeredBus.On(StageEventType.PresentationBeat, OnStageEvent, -20);

        if (logBeatAudio)
            Debug.Log("[StageBeatAudioSystem] Registered stage beat audio handlers.");
    }

    private void Unregister()
    {
        if (_registeredBus == null)
            return;

        _registeredBus.Off(StageEventType.BeforeIntentExecute, OnStageEvent);
        _registeredBus.Off(StageEventType.EntityDamaged, OnStageEvent);
        _registeredBus.Off(StageEventType.PresentationBatchBegin, OnStageEvent);
        _registeredBus.Off(StageEventType.PresentationBeat, OnStageEvent);
        _registeredBus = null;
    }

    private void OnStageEvent(StageEvent evt)
    {
        switch (evt.Type)
        {
            case StageEventType.BeforeIntentExecute:
                TryPlayPlayerBeat(evt);
                break;
            case StageEventType.EntityDamaged:
                Play(enemyHitBeat, $"hit {evt.EntityType}");
                break;
            case StageEventType.PresentationBatchBegin:
            case StageEventType.PresentationBeat:
                PlayEnemyBeat(evt.EnemyBeatKind);
                break;
        }
    }

    private void TryPlayPlayerBeat(StageEvent evt)
    {
        if (evt.EntityType != EntityType.Player)
            return;

        AudioClip clip = evt.IntentType switch
        {
            IntentType.Noop => playerNoopBeat,
            IntentType.Card => playerCardBeat,
            IntentType.Move => IsPlayerPushingBox(evt) ? playerPushBoxBeat : playerMoveBeat,
            _ => null
        };

        Play(clip, $"player {evt.IntentType}");
    }

    private static bool IsPlayerPushingBox(StageEvent evt)
    {
        if (evt.Intent is not MoveIntent moveIntent)
            return false;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized)
            return false;

        Vector2Int target = evt.From + moveIntent.Direction;
        EntityHandle occupant = entitySystem.GetOccupant(target);
        if (!entitySystem.IsValid(occupant))
            return false;

        int index = entitySystem.GetIndex(occupant);
        return index >= 0 && entitySystem.entities.coreComponents[index].EntityType == EntityType.Box;
    }

    private void PlayEnemyBeat(EnemyBeatKind kind)
    {
        AudioClip clip = kind switch
        {
            EnemyBeatKind.Spawn => enemySpawnBeat,
            EnemyBeatKind.Move => enemyMoveBeat,
            EnemyBeatKind.Empty => enemyEmptyBeat,
            _ => null
        };

        Play(clip, $"enemy {kind}");
    }

    private void Play(AudioClip clip, string reason)
    {
        if (clip == null)
        {
            if (logBeatAudio)
                Debug.Log($"[StageBeatAudioSystem] Skip beat audio: {reason}, clip=null.");
            return;
        }

        if (logBeatAudio)
            Debug.Log($"[StageBeatAudioSystem] Play beat audio: {reason}, clip={clip.name}.");

        AudioBus.Ensure().PlaySfx(clip, volumeScale);
    }
}
