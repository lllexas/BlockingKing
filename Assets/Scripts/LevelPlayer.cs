using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum LevelPlayMode
{
    Classic,
    StepLimit,
    Escort
}

public enum LevelDataSource
{
    None,
    Inspector,
    QuickPlaySession,
    RuntimeRequest
}

public enum LevelPlayResult
{
    None,
    Success,
    Failure,
    Cancelled
}

public sealed class LevelPlayRequest
{
    public LevelData Level;
    public TileMappingConfig Config;
    public LevelPlayMode Mode = LevelPlayMode.Classic;
    public int StepLimit = 30;
    public RunDifficultySnapshot Difficulty = RunDifficultySnapshot.Default;
    public RunRewardConfigSO RewardSettings;

    [System.Obsolete("Use Difficulty.EnemySpawnDifficultyProfile instead.")]
    public EnemySpawnDifficultyProfileSO EnemySpawnDifficultyProfile;

    [System.Obsolete("Use Difficulty.OverallDifficulty instead.")]
    public float OverallDifficulty = 1f;

    public int RouteLayer;
    public int RouteLayerCount = 1;
}

/// <summary>
/// 关卡播放入口。生命周期分为：
/// LoadLevel -> RebuildWorld -> StartPlayback -> StopPlayback。
/// Inspector/QuickPlay 只负责提供默认关卡；route/stage 应通过 LevelPlayRequest 显式播放。
/// </summary>
public class LevelPlayer : MonoBehaviour
{
    private const string LevelMeshObjectName = "LevelMesh";
    private const string OutsideDiamondObjectName = "OutsideDiamond";
    private const int DefaultPlayerTagID = 1;
    private const int DefaultBoxTagID = 2;
    private const int DefaultTargetTagID = 3;
    private const int DefaultTargetCoreTagID = 7;
    private const int DefaultTargetEnemyTagID = 8;

    [Header("关卡数据")]
    [SerializeField, Tooltip("直接拖入 LevelData SO（QuickPlaySession 激活时会覆盖它）")]
    private LevelData levelData;

    [SerializeField, Tooltip("直接拖入 TileMappingConfig（可选，为空则无墙壁）")]
    private TileMappingConfig tileConfig;

    [Header("Mesh 参数")]
    public float cellSize = 1f;
    public float wallHeight = 0.4f;
    public float tagMarkerSize = 0.35f;
    public float tagYOffset = 0.02f;

    [Header("渲染")]
    [Tooltip("拖入预设材质。留空则自动创建（重建时复用，属性不丢失）。")]
    public Material material;

    [SerializeField, Tooltip("B版地形渲染：用 GPU Instancing 绘制格子地形，支持 groundMap 变化自动刷新。")]
    private bool useInstancedTerrain = true;

    [Header("ECS")]
    [SerializeField] private int maxEntityCount = 256;

    [Header("Play Mode")]
    [SerializeField] private LevelPlayMode defaultPlayMode = LevelPlayMode.Classic;
    [SerializeField, Min(1)] private int defaultStepLimit = 30;

    [Header("Escort Rewards")]
    [SerializeField, Min(0)] private int escortRewardBoxGold = 2;
    [SerializeField, Min(0)] private int escortCompletionGold = 5;
    [SerializeField] private List<CardSO> escortCompletionCardRewards = new List<CardSO>();

    private LevelData _level;
    private TileMappingConfig _config;
    private GameObject _meshGO;
    private GameObject _outsideDiamondGO;
    private Material _materialInstance;
    private Material _outsideDiamondMaterial;
    private LevelDataSource _levelSource;
    private ILevelPlayRule _playRule;
    private bool _isPlaying;
    private bool _isSettled;
    private LevelPlayMode _playMode;
    private LevelPlayResult _lastResult;
    private int _remainingSteps = -1;
    private RunDifficultySnapshot _activeDifficulty = RunDifficultySnapshot.Default;
    private RunRewardConfigSO _activeRewardSettings;
    private GUIStyle _stepLimitStyle;
    private GUIStyle _stepLimitShadowStyle;
    private GUIStyle _classicPhaseStyle;
    private GUIStyle _classicPhaseShadowStyle;
    private GUIStyle _casinoTitleStyle;
    private GUIStyle _casinoTitleShadowStyle;
    private GUIStyle _casinoGoldStyle;
    private GUIStyle _casinoGoldShadowStyle;
    private GUIStyle _casinoDetailStyle;
    private GUIStyle _casinoPanelStyle;
    private readonly HashSet<Vector2Int> _targetCells = new();
    private readonly HashSet<Vector2Int> _coreTargetCells = new();

    public LevelData CurrentLevel => _level;
    public TileMappingConfig CurrentConfig => _config;
    public LevelDataSource CurrentLevelSource => _levelSource;
    public LevelPlayMode PlayMode => _playMode;
    public LevelPlayResult LastResult => _lastResult;
    public int RemainingSteps => _remainingSteps;
    public bool IsPlaying => _isPlaying;

    private void Start()
    {
        var flow = GameFlowController.Instance;
        if (flow != null && !flow.ShouldLevelPlayerAutoBuild)
            return;

        if (LoadConfiguredLevel())
        {
            RebuildWorld();
            flow?.EnterLevel();
        }
    }

    private void OnDestroy()
    {
        StopPlayback();
    }

    private void OnGUI()
    {
        if (!_isPlaying)
            return;

        if (_playMode == LevelPlayMode.StepLimit)
        {
            EnsureStepLimitStyles();

            string text = $"Steps: {Mathf.Max(0, _remainingSteps)}";
            float width = Mathf.Min(460f, Screen.width - 32f);
            var rect = new Rect((Screen.width - width) * 0.5f, 18f, width, 58f);
            var shadowRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height);

            GUI.Label(shadowRect, text, _stepLimitShadowStyle);
            GUI.Label(rect, text, _stepLimitStyle);
            return;
        }

        if (_playRule is ClassicPlayRule classicRule)
            classicRule.DrawGUI(this);
    }

    public bool PlayLevel(LevelData level)
    {
        return PlayLevel(new LevelPlayRequest
        {
            Level = level,
            Config = tileConfig,
            Mode = defaultPlayMode,
            StepLimit = defaultStepLimit
        });
    }

    public bool PlayLevel(LevelData level, LevelPlayMode mode)
    {
        return PlayLevel(new LevelPlayRequest
        {
            Level = level,
            Config = tileConfig,
            Mode = mode,
            StepLimit = defaultStepLimit
        });
    }

    public bool PlayLevel(LevelData level, LevelPlayMode mode, int maxSteps)
    {
        return PlayLevel(new LevelPlayRequest
        {
            Level = level,
            Config = tileConfig,
            Mode = mode,
            StepLimit = maxSteps
        });
    }

    public bool PlayLevel(LevelPlayRequest request)
    {
        if (request == null)
        {
            Debug.LogError("[LevelPlayer] PlayLevel failed: request is null.");
            return false;
        }

        StopPlayback();

        if (!LoadLevel(request.Level, request.Config != null ? request.Config : tileConfig, LevelDataSource.RuntimeRequest))
            return false;

        RebuildWorld();
        StartPlayback(request);
        return true;
    }

    public bool LoadLevel(LevelData level, TileMappingConfig config, LevelDataSource source)
    {
        if (level == null)
        {
            Debug.LogWarning("[LevelPlayer] LoadLevel failed: level is null.");
            return false;
        }

        _level = level;
        _config = config;
        _config?.RebuildCache();
        _levelSource = source;

        if (_config == null)
            Debug.LogWarning("[LevelPlayer] TileMappingConfig is missing. Tag IDs will use project defaults.");

        Debug.Log($"[LevelPlayer] Level loaded: {level.name}, source={source}");
        return true;
    }

    public bool LoadConfiguredLevel()
    {
        var session = Resources.Load<QuickPlaySession>("QuickPlaySession");
        if (session != null && session.active && session.targetLevel != null)
        {
            bool loaded = LoadLevel(
                session.targetLevel,
                session.config != null ? session.config : tileConfig,
                LevelDataSource.QuickPlaySession);

            session.active = false;
            return loaded;
        }

        return LoadLevel(levelData, tileConfig, LevelDataSource.Inspector);
    }

    public void RebuildWorld()
    {
        if (_level == null)
        {
            Debug.LogWarning("[LevelPlayer] RebuildWorld failed: no loaded level.");
            return;
        }

        BuildEntitiesInternal();
        CacheTargetCells();
        RebuildTerrainVisualsInternal();
    }

    public void StartPlayback(LevelPlayRequest request)
    {
        if (_level == null)
        {
            Debug.LogWarning("[LevelPlayer] StartPlayback failed: no loaded level.");
            return;
        }

        _playMode = request != null ? request.Mode : defaultPlayMode;
        _activeDifficulty = ResolveDifficulty(request);
        _activeRewardSettings = ResolveRewardSettings(request);
        _playRule = CreatePlayRule(_playMode);
        _isPlaying = true;
        _isSettled = false;
        _lastResult = LevelPlayResult.None;

        _playRule.Begin(this, request);
        ConfigureSpawnDifficulty(request, _activeDifficulty);

        GameFlowController.Instance?.EnterLevel();
        TickSystem.OnTick -= HandleTick;
        TickSystem.OnTick += HandleTick;

        if (_playRule.ShouldStartSpawningOnBegin)
            SpawnSystem.Instance?.StartSpawning();
        else
            SpawnSystem.Instance?.StopSpawning();

        Debug.Log($"[LevelPlayer] Playback started: {_level.levelName}, mode={_playMode}, steps={_remainingSteps}");
        _playRule.Evaluate(this);
    }

    public void StopPlayback()
    {
        TickSystem.OnTick -= HandleTick;
        _playRule?.End(this);
        SpawnSystem.Instance?.StopSpawning();
        HandZone.SetCardsLocked(false);
        _playRule = null;
        _isPlaying = false;
        _activeDifficulty = RunDifficultySnapshot.Default;
        _activeRewardSettings = null;
    }

    [Button("Rebuild World", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void RebuildWorldFromInspector()
    {
        if (_level == null && !LoadConfiguredLevel())
            return;

        RebuildWorld();
    }

    [Button("Rebuild Mesh", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void BuildMesh()
    {
        if (_level == null && !LoadConfiguredLevel())
            return;

        if (useInstancedTerrain)
        {
            if (EntitySystem.Instance == null || !EntitySystem.Instance.IsInitialized)
                BuildEntitiesInternal();

            CacheTargetCells();
            RebuildTerrainVisualsInternal();
        }
        else
        {
            BuildMeshInternal();
        }
    }

    [Button("Restart Level", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void RestartLevel()
    {
        PlayLevel(new LevelPlayRequest
        {
            Level = _level != null ? _level : levelData,
            Config = _config != null ? _config : tileConfig,
            Mode = _playMode,
            StepLimit = _remainingSteps > 0 ? _remainingSteps : defaultStepLimit
        });
    }

    public void BuildEntities()
    {
        if (_level == null && !LoadConfiguredLevel())
            return;

        BuildEntitiesInternal();
        CacheTargetCells();
    }

    [Button("Clear Mesh", ButtonSizes.Medium), HorizontalGroup("Buttons")]
    public void ClearMesh()
    {
        int removedCount = ClearLevelMeshObjects();
        DisableInstancedTerrainRenderer();
        if (removedCount == 0)
        {
            Debug.LogWarning("[LevelPlayer] 没有可清除的 LevelMesh。Instanced terrain renderer was disabled if present.");
            return;
        }

        Debug.Log($"[LevelPlayer] Mesh 已清除，数量={removedCount}");
    }

    internal bool AreAllBoxesOnTargets()
    {
        if (_targetCells.Count == 0)
            return false;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        var entities = entitySystem.entities;
        bool hasBox = false;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Box)
                continue;

            hasBox = true;
            if (!_targetCells.Contains(entities.coreComponents[i].Position))
                return false;
        }

        return hasBox;
    }

    internal int CountBoxesOnTargets()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return 0;

        int count = 0;
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType == EntityType.Box &&
                _targetCells.Contains(entities.coreComponents[i].Position))
            {
                count++;
            }
        }

        return count;
    }

    internal int CountBoxesOnTargetsExcluding(HashSet<int> excludedBoxIds)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return 0;

        int count = 0;
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var core = ref entities.coreComponents[i];
            if (core.EntityType != EntityType.Box || !_targetCells.Contains(core.Position))
                continue;

            if (excludedBoxIds != null && excludedBoxIds.Contains(core.Id))
                continue;

            count++;
        }

        return count;
    }

    internal void CollectBoxIdsOnTargets(HashSet<int> results)
    {
        if (results == null)
            return;

        results.Clear();
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var core = ref entities.coreComponents[i];
            if (core.EntityType == EntityType.Box && _targetCells.Contains(core.Position))
                results.Add(core.Id);
        }
    }

    internal int CountEnemiesAlive()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return 0;

        int count = 0;
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType == EntityType.Enemy)
                count++;
        }

        return count;
    }

    internal bool HasAnyEnemyAlive()
    {
        return CountEnemiesAlive() > 0;
    }

    internal bool IsAnyCoreBoxAlive()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType == EntityType.Box &&
                entities.propertyComponents[i].IsCore)
            {
                return true;
            }
        }

        return false;
    }

    internal bool IsAnyCoreBoxOnTarget()
    {
        if (_coreTargetCells.Count == 0)
            return false;

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Box ||
                !entities.propertyComponents[i].IsCore)
            {
                continue;
            }

            if (_coreTargetCells.Contains(entities.coreComponents[i].Position))
                return true;
        }

        return false;
    }

    internal void SetRemainingSteps(int value)
    {
        _remainingSteps = value;
    }

    internal void SettleLevel(LevelPlayResult result, string reason)
    {
        if (_isSettled)
            return;

        _isSettled = true;
        _lastResult = result;
        StopPlayback();

        Debug.Log($"[LevelPlayer] Level settled: result={result}, reason={reason}");

        var stageFacade = NekoGraph.GraphHub.Instance?.GetFacade<RunStageFacade>();
        if (stageFacade != null && stageFacade.HasWaitingStage())
            stageFacade.ResumeWaitingStage(result == LevelPlayResult.Success ? 0 : 1);

        GameFlowController.Instance?.OnRouteClassicLevelSettled(result);
    }

    internal int AwardGoldByArithmeticSequence(int count, int firstAmount, int stepAmount, string reason)
    {
        if (count <= 0)
            return 0;

        int amount = 0;
        for (int i = 0; i < count; i++)
            amount += firstAmount + i * stepAmount;

        AwardGold(amount, reason);
        return amount;
    }

    internal bool AwardGold(int amount, string reason)
    {
        if (amount <= 0)
            return false;

        var inventory = NekoGraph.GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            NekoGraph.GraphHub.Instance?.RegisterFacade(inventory);
        }

        if (inventory == null || !inventory.AddGold(amount))
        {
            Debug.LogWarning($"[LevelPlayer] Gold reward skipped: amount={amount}, reason={reason}");
            return false;
        }

        Debug.Log($"[LevelPlayer] Gold awarded: amount={amount}, reason={reason}");
        return true;
    }

    internal bool TryConsumeEscortRewardBox(EntityHandle boxHandle, Vector2Int cell)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(boxHandle))
            return false;

        int boxIndex = entitySystem.GetIndex(boxHandle);
        if (boxIndex < 0 ||
            entitySystem.entities.coreComponents[boxIndex].EntityType != EntityType.Box ||
            entitySystem.entities.propertyComponents[boxIndex].IsCore)
        {
            return false;
        }

        if (!TryFindEscortRewardTargetAtCell(entitySystem, cell, out var targetHandle))
            return false;

        int gold = GetEscortRewardBoxGold();
        AwardGold(gold, "escort reward box");
        entitySystem.DestroyEntity(boxHandle);
        if (entitySystem.IsValid(targetHandle))
            entitySystem.DestroyEntity(targetHandle);

        Debug.Log($"[LevelPlayer] Escort reward box consumed at {cell}, gold={gold}");
        return true;
    }

    internal void AwardEscortCompletionRewards()
    {
        AwardGold(GetEscortCompletionGold(), "escort completion");

        var cardRewards = GetEscortCompletionCardRewards();
        if (cardRewards == null || cardRewards.Count == 0)
            return;

        var deck = NekoGraph.GraphHub.Instance?.GetFacade<CardDeckFacade>();
        if (deck == null)
        {
            deck = new CardDeckFacade();
            NekoGraph.GraphHub.Instance?.RegisterFacade(deck);
        }

        if (deck == null)
        {
            Debug.LogWarning("[LevelPlayer] Escort card rewards skipped because CardDeckFacade is missing.");
            return;
        }

        int granted = 0;
        for (int i = 0; i < cardRewards.Count; i++)
        {
            var card = cardRewards[i];
            if (card == null)
                continue;

            if (deck.AddCard(card, 1))
                granted++;
        }

        Debug.Log($"[LevelPlayer] Escort card rewards granted: {granted}");
    }

    internal int ConvertUnoccupiedClassicTargetsToEnemyTargets()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return 0;

        int targetTagID = ResolveTagID("target", DefaultTargetTagID);
        int targetEnemyTagID = ResolveTagID("Target.Enemy", DefaultTargetEnemyTagID);
        if (targetEnemyTagID <= 0)
            return 0;

        // 先收集所有待转换的实体ID（避免遍历中修改数组）
        var idsToConvert = new List<int>();
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Target)
                continue;
            if (entities.propertyComponents[i].SourceTagId != targetTagID)
                continue;
            var pos = entities.coreComponents[i].Position;
            if (!_targetCells.Contains(pos) || IsBoxOnCell(entitySystem, pos))
                continue;
            idsToConvert.Add(entities.coreComponents[i].Id);
        }

        var terrainDrawSystem = GetComponent<TerrainDrawSystem>();
        int converted = 0;

        foreach (int entityId in idsToConvert)
        {
            var handle = entitySystem.GetHandleFromId(entityId);
            if (!entitySystem.IsValid(handle))
                continue;

            int index = entitySystem.GetIndex(handle);
            if (index < 0)
                continue;

            var pos = entities.coreComponents[index].Position;
            if (IsBoxOnCell(entitySystem, pos))
                continue;

            // 销毁旧 Target 实体，走正式流程重建为 Target.Enemy
            entitySystem.DestroyEntity(handle);

            var newHandle = CreateTaggedEntity(entitySystem, EntityType.Target, pos, targetEnemyTagID, false);
            SetupEnemyTargetCounter(entitySystem, newHandle, targetEnemyTagID);
            converted++;

            SpawnSystem.Instance?.SpawnImmediatelyFromTarget(newHandle);

            terrainDrawSystem?.ReplaceTargetTag(pos.x, pos.y, targetTagID, targetEnemyTagID);
        }

        Debug.Log($"[LevelPlayer] Converted classic targets to Target.Enemy (destroy+recreate): {converted}");
        return converted;
    }

    private void BuildMeshInternal()
    {
        DisableInstancedTerrainRenderer();
        Debug.Log($"[LevelPlayer] 开始构建: {_level.levelName}, size={_level.width}x{_level.height}, wallHeight={wallHeight}");

        ClearLevelMeshObjects();

        var builder = new LevelMeshBuilder
        {
            cellSize = cellSize,
            wallHeight = wallHeight,
            tagMarkerSize = tagMarkerSize,
            tagYOffset = tagYOffset,
        };

        Mesh mesh = builder.Build(_level.GetMap2D(), _config, GetLevelMarkerTags());
        if (mesh == null)
        {
            Debug.LogWarning("[LevelPlayer] 构建失败: mesh 为空");
            return;
        }

        _meshGO = new GameObject(LevelMeshObjectName);
        _meshGO.transform.SetParent(transform);
        _meshGO.AddComponent<MeshFilter>().mesh = mesh;

        EnsureLevelMaterial();
        _meshGO.AddComponent<MeshRenderer>().sharedMaterial = _materialInstance;

        var camCtrl = Camera.main?.GetComponent<CameraController>();
        if (camCtrl != null)
        {
            camCtrl.SetMapBounds(_level.width, _level.height);
            camCtrl.ResetView();
        }

        EnsureOutsideDiamond(_level.width * cellSize, _level.height * cellSize);

        Debug.Log($"[LevelPlayer] {_level.levelName} 已构建, 顶点={mesh.vertexCount}, wallHeight={wallHeight}");
    }

    private void RebuildTerrainVisualsInternal()
    {
        EnsureLevelMaterial();

        if (!useInstancedTerrain)
        {
            DisableInstancedTerrainRenderer();
            BuildMeshInternal();
            return;
        }

        ClearLevelMeshObjects();

        var terrainDrawSystem = EnsureRuntimeSystem<TerrainDrawSystem>();
        terrainDrawSystem.enabled = true;
        terrainDrawSystem.Configure(
            _config,
            _materialInstance,
            cellSize,
            wallHeight,
            tagMarkerSize,
            tagYOffset,
            GetLevelMarkerTags());

        var overlayDrawSystem = GridOverlayDrawSystem.Instance != null
            ? GridOverlayDrawSystem.Instance
            : EnsureRuntimeSystem<GridOverlayDrawSystem>();
        overlayDrawSystem.ConfigureSurfaceHeights(wallHeight);

        var camCtrl = Camera.main?.GetComponent<CameraController>();
        if (camCtrl != null)
        {
            camCtrl.SetMapBounds(_level.width, _level.height);
            camCtrl.ResetView();
        }

        EnsureOutsideDiamond(_level.width * cellSize, _level.height * cellSize);

        Debug.Log($"[LevelPlayer] Instanced terrain ready: {_level.levelName}, size={_level.width}x{_level.height}");
    }

    private void DisableInstancedTerrainRenderer()
    {
        var terrainDrawSystem = GetComponent<TerrainDrawSystem>();
        if (terrainDrawSystem == null)
            return;

        terrainDrawSystem.Clear();
        terrainDrawSystem.enabled = false;
    }

    private void BuildEntitiesInternal()
    {
        EnsureLevelMaterial();

        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null)
            entitySystem = gameObject.AddComponent<EntitySystem>();

        EnsureRuntimeSystem<EventBusSystem>();
        EnsureRuntimeSystem<IntentSystem>();
        EnsureRuntimeSystem<MoveSystem>();
        EnsureRuntimeSystem<AttackSystem>();
        var cardEffectSystem = EnsureRuntimeSystem<CardEffectSystem>();
        EnsureRuntimeSystem<CombatResolutionSystem>();
        var enemyAutoAISystem = EnsureRuntimeSystem<EnemyAutoAISystem>();
        var auraResolutionSystem = EnsureRuntimeSystem<AuraResolutionSystem>();
        var drawSystem = EnsureRuntimeSystem<DrawSystem>();
        EnsureRuntimeSystem<EnemyIntentOverlaySystem>();
        EnsureRuntimeSystem<UserInputReader>();
        drawSystem.SetWallMaterial(_materialInstance);

        entitySystem.Initialize(maxEntityCount, _level.width, _level.height);
        IntentSystem.Instance?.Clear();
        entitySystem.SetTerrain(_level.GetMap2D());
        entitySystem.SetWallTerrainIds(GetWallTerrainIds(), GetDefaultFloorTerrainId());

        int playerTagID = ResolveTagID("player", DefaultPlayerTagID);
        int boxTagID = ResolveTagID("box", DefaultBoxTagID);
        int boxCoreTagID = ResolveTagID("Box.Core", -1);
        int targetTagID = ResolveTagID("target", DefaultTargetTagID);
        int targetCoreTagID = ResolveTagID("Target.Core", DefaultTargetCoreTagID);
        int targetEnemyTagID = ResolveTagID("Target.Enemy", DefaultTargetEnemyTagID);
        int enemyGoTagID = ResolveTagID("Enemy.Go", 4);
        int enemyGrenadierTagID = ResolveTagID("Enemy.Grenadier", -1);
        int enemyCrossbowTagID = ResolveTagID("Enemy.Crossbow", -1);
        int enemyArtilleryTagID = ResolveTagID("Enemy.Artillery", -1);
        int enemyCurseCasterTagID = ResolveTagID("Enemy.CurseCaster", -1);
        int enemyGuokuiTagID = ResolveTagID("Enemy.Guokui", -1);
        int enemyErtongTagID = ResolveTagID("Enemy.Ertong", -1);
        int unstableWallTagID = ResolveTagID("Wall.Unstable", -1);
        ConfigureCardWallHealth(cardEffectSystem, unstableWallTagID);
        ConfigureEnemyWallBreakHealth(enemyAutoAISystem, unstableWallTagID);
        enemyAutoAISystem.ConfigureSpecialEnemyTags(enemyGrenadierTagID, enemyCrossbowTagID, enemyArtilleryTagID);
        enemyAutoAISystem.ConfigureAdvancedEnemyTags(
            enemyCurseCasterTagID,
            enemyGuokuiTagID,
            enemyErtongTagID,
            _config != null && enemyGuokuiTagID > 0 ? _config.GetTagEntityBP(enemyGuokuiTagID) : null);
        int enemyTargetCount = 0;

        foreach (var tag in _level.tags)
        {
            var pos = new Vector2Int(tag.x, tag.y);
            if (tag.tagID == playerTagID)
                CreateTaggedEntity(entitySystem, EntityType.Player, pos, tag.tagID);
            else if (tag.tagID == boxTagID)
                CreateTaggedEntity(entitySystem, EntityType.Box, pos, tag.tagID);
            else if (boxCoreTagID > 0 && tag.tagID == boxCoreTagID)
                CreateCoreBox(entitySystem, pos, tag.tagID);
            else if (tag.tagID == targetTagID)
                CreateTaggedEntity(entitySystem, EntityType.Target, pos, tag.tagID, false);
            else if (targetCoreTagID > 0 && tag.tagID == targetCoreTagID)
                CreateTaggedEntity(entitySystem, EntityType.Target, pos, tag.tagID, false);
            else if (targetEnemyTagID > 0 && tag.tagID == targetEnemyTagID)
            {
                var targetHandle = CreateTaggedEntity(entitySystem, EntityType.Target, pos, tag.tagID, false);
                SetupEnemyTargetCounter(entitySystem, targetHandle, tag.tagID);
                enemyTargetCount++;
            }
            else if (IsEnemyTag(
                         tag.tagID,
                         enemyGoTagID,
                         enemyGrenadierTagID,
                         enemyCrossbowTagID,
                         enemyArtilleryTagID,
                         enemyCurseCasterTagID,
                         enemyGuokuiTagID,
                         enemyErtongTagID))
                CreateTaggedEntity(entitySystem, EntityType.Enemy, pos, tag.tagID);
            else if (unstableWallTagID > 0 && tag.tagID == unstableWallTagID)
                CreateUnstableWall(entitySystem, pos, tag.tagID);
        }

        var spawnSystem = EnsureRuntimeSystem<SpawnSystem>();
        spawnSystem.Initialize(_config?.GetTagEntityBP(enemyGoTagID), enemyGoTagID);

        Debug.Log($"[LevelPlayer] ECS 已初始化，实体数={entitySystem.entities.entityCount}, Target.Enemy={enemyTargetCount}, targetEnemyTagID={targetEnemyTagID}");
    }

    private void ConfigureSpawnDifficulty(LevelPlayRequest request, RunDifficultySnapshot difficulty)
    {
        var spawnSystem = SpawnSystem.Instance ?? GetComponent<SpawnSystem>();
        if (spawnSystem == null)
            return;

        int routeLayer = request != null ? request.RouteLayer : 0;
        int routeLayerCount = request != null ? request.RouteLayerCount : 1;
        var flow = GameFlowController.Instance;
        if (routeLayerCount <= 1 && flow != null)
            routeLayerCount = flow.RouteLayerCount;

        spawnSystem.ConfigureDifficultyProfile(
            difficulty.EnemySpawnDifficultyProfile,
            difficulty.OverallDifficulty,
            routeLayer,
            routeLayerCount);
    }

    private RunDifficultySnapshot ResolveDifficulty(LevelPlayRequest request)
    {
        int routeLayer = request != null ? request.RouteLayer : 0;
        int routeLayerCount = request != null ? request.RouteLayerCount : 1;
        var flow = GameFlowController.Instance;
        if (routeLayerCount <= 1 && flow != null)
            routeLayerCount = flow.RouteLayerCount;

        if (request != null)
        {
            var snapshot = request.Difficulty;
#pragma warning disable CS0618
            if (snapshot.EnemySpawnDifficultyProfile == null)
                snapshot.EnemySpawnDifficultyProfile = request.EnemySpawnDifficultyProfile;
            if (snapshot.OverallDifficulty <= 0f && request.OverallDifficulty > 0f)
                snapshot.OverallDifficulty = request.OverallDifficulty;
#pragma warning restore CS0618
            if (snapshot.EnemySpawnDifficultyProfile != null || snapshot.OverallDifficulty > 0f)
            {
                if (snapshot.EnemyHealthMultiplier <= 0f)
                    snapshot.EnemyHealthMultiplier = 1f;
                if (snapshot.EnemyAttackMultiplier <= 0f)
                    snapshot.EnemyAttackMultiplier = 1f;
                if (snapshot.RewardMultiplier <= 0f)
                    snapshot.RewardMultiplier = 1f;
                snapshot.Progress = routeLayerCount > 1
                    ? Mathf.Clamp01(routeLayer / (float)(routeLayerCount - 1))
                    : snapshot.Progress;
                return snapshot;
            }
        }

        return flow != null
            ? flow.BuildDifficultySnapshot(routeLayer, routeLayerCount)
            : RunDifficultySnapshot.Default;
    }

    private RunRewardConfigSO ResolveRewardSettings(LevelPlayRequest request)
    {
        return request != null && request.RewardSettings != null
            ? request.RewardSettings
            : GameFlowController.Instance != null ? GameFlowController.Instance.RewardSettings : null;
    }

    private int GetEscortRewardBoxGold()
    {
        return _activeRewardSettings != null
            ? _activeRewardSettings.GetEscortRewardBoxGold(_activeDifficulty)
            : escortRewardBoxGold;
    }

    private int GetEscortCompletionGold()
    {
        return _activeRewardSettings != null
            ? _activeRewardSettings.GetEscortCompletionGold(_activeDifficulty)
            : escortCompletionGold;
    }

    private IReadOnlyList<CardSO> GetEscortCompletionCardRewards()
    {
        return _activeRewardSettings != null
            ? _activeRewardSettings.escortCompletionCardRewards
            : escortCompletionCardRewards;
    }

    private void HandleTick()
    {
        if (!_isPlaying || _isSettled || _playRule == null)
            return;

        _playRule.OnTick(this);
        _playRule.Evaluate(this);
    }

    private ILevelPlayRule CreatePlayRule(LevelPlayMode mode)
    {
        return mode switch
        {
            LevelPlayMode.StepLimit => new StepLimitPlayRule(),
            LevelPlayMode.Escort => new EscortPlayRule(),
            _ => new ClassicPlayRule()
        };
    }

    private void EnsureStepLimitStyles()
    {
        _stepLimitStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.92f, 0.45f, 1f) }
        };

        _stepLimitShadowStyle ??= new GUIStyle(_stepLimitStyle)
        {
            normal = { textColor = new Color(0f, 0f, 0f, 0.72f) }
        };
    }

    private void EnsureClassicPhaseStyles()
    {
        _classicPhaseStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 26,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.95f, 0.72f, 1f) }
        };

        _classicPhaseShadowStyle ??= new GUIStyle(_classicPhaseStyle)
        {
            normal = { textColor = new Color(0f, 0f, 0f, 0.72f) }
        };
    }

    private void EnsureCasinoStyles()
    {
        _casinoTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.9f, 0.4f, 1f) }
        };

        _casinoTitleShadowStyle ??= new GUIStyle(_casinoTitleStyle)
        {
            normal = { textColor = new Color(0f, 0f, 0f, 0.8f) }
        };

        _casinoGoldStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 40,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(1f, 0.85f, 0.05f, 1f) }
        };

        _casinoGoldShadowStyle ??= new GUIStyle(_casinoGoldStyle)
        {
            normal = { textColor = new Color(0f, 0f, 0f, 0.8f) }
        };

        _casinoDetailStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 15,
            fontStyle = FontStyle.Normal,
            normal = { textColor = new Color(1f, 0.85f, 0.4f, 0.85f) }
        };

        if (_casinoPanelStyle == null)
        {
            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.05f, 0.02f, 0f, 0.82f));
            bgTex.Apply();
            _casinoPanelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = bgTex },
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
        }
    }

    private T EnsureRuntimeSystem<T>() where T : Component
    {
        var system = GetComponent<T>();
        if (system == null)
            system = gameObject.AddComponent<T>();
        return system;
    }

    private int ClearLevelMeshObjects()
    {
        int removedCount = 0;

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null)
                continue;

            if (!string.Equals(child.name, LevelMeshObjectName, System.StringComparison.Ordinal))
                continue;

            CaptureReusableMaterial(child.gameObject);
            DestroyUnityObjectImmediate(child.gameObject);
            removedCount++;
        }

        if (_meshGO != null)
        {
            CaptureReusableMaterial(_meshGO);
            DestroyUnityObjectImmediate(_meshGO);
            removedCount++;
        }

        if (_outsideDiamondGO != null)
        {
            DestroyUnityObjectImmediate(_outsideDiamondGO);
            removedCount++;
        }

        _meshGO = null;
        _outsideDiamondGO = null;
        return removedCount;
    }

    private void CaptureReusableMaterial(GameObject meshObject)
    {
        if (meshObject == null || material != null)
            return;

        var mr = meshObject.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != null)
            _materialInstance = mr.sharedMaterial;
    }

    private static void DestroyUnityObjectImmediate(UnityEngine.Object obj)
    {
        if (obj == null)
            return;

        DestroyImmediate(obj);
    }

    private void EnsureLevelMaterial()
    {
        if (material != null)
        {
            _materialInstance = material;
            return;
        }

        if (_materialInstance == null)
            _materialInstance = new Material(Shader.Find("BlockingKing/LevelGeometric")
                                          ?? Shader.Find("Universal Render Pipeline/Lit"));
    }

    private void EnsureOutsideDiamond(float mapWidth, float mapHeight)
    {
        if (_outsideDiamondMaterial == null)
        {
            var shader = Shader.Find("BlockingKing/OutsideDiamond")
                         ?? Shader.Find("Universal Render Pipeline/Unlit");
            _outsideDiamondMaterial = new Material(shader)
            {
                name = "OutsideDiamond_Runtime",
                enableInstancing = true
            };
        }

        if (_outsideDiamondGO != null)
            return;

        // 构建大 Quad，覆盖地图外可见区域
        float maxExtent = Mathf.Max(mapWidth, mapHeight) * cellSize;
        float halfSize = maxExtent * 2.5f; // 地图最大边长的 2.5 倍 = 充裕的 padding

        var mesh = new Mesh { name = "OutsideDiamondQuad" };
        float h = halfSize;
        mesh.SetVertices(new[]
        {
            new Vector3(-h, 0f, -h),
            new Vector3( h, 0f, -h),
            new Vector3(-h, 0f,  h),
            new Vector3( h, 0f,  h)
        });
        mesh.SetNormals(new[]
        {
            Vector3.up, Vector3.up, Vector3.up, Vector3.up
        });
        mesh.SetTriangles(new[] { 0, 1, 2, 1, 3, 2 }, 0);
        mesh.RecalculateBounds();

        _outsideDiamondGO = new GameObject(OutsideDiamondObjectName);
        _outsideDiamondGO.transform.SetParent(transform);
        _outsideDiamondGO.transform.position = new Vector3(
            mapWidth * cellSize * 0.5f,
            -0.01f,  // 略低于地板，地板存在处被深度遮挡
            mapHeight * cellSize * 0.5f);
        _outsideDiamondGO.AddComponent<MeshFilter>().mesh = mesh;
        _outsideDiamondGO.AddComponent<MeshRenderer>().sharedMaterial = _outsideDiamondMaterial;
    }

    private EntityHandle CreateTaggedEntity(
        EntitySystem entitySystem,
        EntityType entityType,
        Vector2Int pos,
        int tagId,
        bool occupiesGrid = true,
        bool publishCreatedEvent = true)
    {
        var handle = entitySystem.CreateEntity(entityType, pos, occupiesGrid);
        ApplyEntityBP(entitySystem, handle, tagId);
        if (publishCreatedEvent)
            entitySystem.PublishEntityCreated(handle);
        return handle;
    }

    private void CreateCoreBox(EntitySystem entitySystem, Vector2Int pos, int tagId)
    {
        var handle = CreateTaggedEntity(entitySystem, EntityType.Box, pos, tagId, publishCreatedEvent: false);
        int index = entitySystem.GetIndex(handle);
        if (index >= 0)
            entitySystem.entities.propertyComponents[index].IsCore = true;

        entitySystem.PublishEntityCreated(handle);
    }

    private void CreateUnstableWall(EntitySystem entitySystem, Vector2Int pos, int tagId)
    {
        CreateTaggedEntity(entitySystem, EntityType.Wall, pos, tagId);
        entitySystem.SetTerrain(pos, GetDefaultFloorTerrainId());
    }

    private void ApplyEntityBP(EntitySystem entitySystem, EntityHandle handle, int tagId)
    {
        int index = entitySystem.GetIndex(handle);
        if (index < 0)
            return;

        EntityBP bp = _config != null ? _config.GetTagEntityBP(tagId) : null;
        ref var properties = ref entitySystem.entities.propertyComponents[index];
        properties.SourceTagId = tagId;
        properties.SourceBP = bp;

        if (bp == null)
            return;

        ref var status = ref entitySystem.entities.statusComponents[index];
        status.BaseMaxHealth = Mathf.Max(1, bp.health);
        status.BaseAttack = Mathf.Max(0, bp.attack);
        status.DamageTaken = 0;
        status.AttackModifier = 0;
        status.MaxHealthModifier = 0;
        properties.Attack = CombatStats.GetAttack(status);
    }

    private void SetupEnemyTargetCounter(EntitySystem entitySystem, EntityHandle handle, int tagId)
    {
        int index = entitySystem.GetIndex(handle);
        if (index < 0)
            return;

        SetupEnemyTargetCounter(entitySystem, index, tagId);
    }

    private void SetupEnemyTargetCounter(EntitySystem entitySystem, int index, int tagId, int initialDelay = -1)
    {
        if (entitySystem == null || entitySystem.entities == null || index < 0 || index >= entitySystem.entities.entityCount)
            return;

        ref var counter = ref entitySystem.entities.counterComponents[index];
        ref var props = ref entitySystem.entities.propertyComponents[index];

        EntityBP bp = _config != null ? _config.GetTagEntityBP(tagId) : null;
        props.SpawnInterval = bp != null && bp.spawnInterval > 0 ? bp.spawnInterval : 3;
        props.SpawnEntityBP = bp != null ? bp.spawnEntityBP : null;

        counter.NextTick = entitySystem.GlobalTick + (initialDelay >= 0 ? initialDelay : props.SpawnInterval);
        Debug.Log($"[LevelPlayer] Target.Enemy counter armed: index={index}, interval={props.SpawnInterval}, nextTick={counter.NextTick}, spawnBP={(props.SpawnEntityBP != null ? props.SpawnEntityBP.name : "<default>")}");
    }

    private static bool IsBoxOnCell(EntitySystem entitySystem, Vector2Int cell)
    {
        if (entitySystem == null || !entitySystem.IsInitialized)
            return false;

        var handle = entitySystem.GetOccupant(cell);
        if (!entitySystem.IsValid(handle))
            return false;

        int index = entitySystem.GetIndex(handle);
        return index >= 0 &&
               entitySystem.entities.coreComponents[index].EntityType == EntityType.Box;
    }

    private bool TryFindEscortRewardTargetAtCell(EntitySystem entitySystem, Vector2Int cell, out EntityHandle targetHandle)
    {
        targetHandle = EntityHandle.None;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        int targetTagID = ResolveTagID("target", DefaultTargetTagID);
        int targetEnemyTagID = ResolveTagID("Target.Enemy", DefaultTargetEnemyTagID);
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Target ||
                entities.coreComponents[i].Position != cell)
            {
                continue;
            }

            int sourceTagId = entities.propertyComponents[i].SourceTagId;
            if (sourceTagId != targetTagID && sourceTagId != targetEnemyTagID)
                continue;

            targetHandle = entitySystem.GetHandleFromId(entities.coreComponents[i].Id);
            return entitySystem.IsValid(targetHandle);
        }

        return false;
    }

    private void ConfigureCardWallHealth(CardEffectSystem cardEffectSystem, int unstableWallTagID)
    {
        if (cardEffectSystem == null || unstableWallTagID <= 0 || _config == null)
            return;

        EntityBP wallBP = _config.GetTagEntityBP(unstableWallTagID);
        if (wallBP == null)
            return;

        cardEffectSystem.ConfigureMaterializedTerrainWallHealth(wallBP.health);
    }

    private void ConfigureEnemyWallBreakHealth(EnemyAutoAISystem enemyAutoAISystem, int unstableWallTagID)
    {
        if (enemyAutoAISystem == null || unstableWallTagID <= 0 || _config == null)
            return;

        EntityBP wallBP = _config.GetTagEntityBP(unstableWallTagID);
        if (wallBP == null)
            return;

        enemyAutoAISystem.ConfigureMaterializedTerrainWallHealth(wallBP.health);
    }

    private List<LevelTagEntry> GetLevelMarkerTags()
    {
        var result = new List<LevelTagEntry>();
        if (_level?.tags == null)
            return result;

        int targetTagID = ResolveTagID("target", DefaultTargetTagID);
        int targetCoreTagID = ResolveTagID("Target.Core", DefaultTargetCoreTagID);
        int targetEnemyTagID = ResolveTagID("Target.Enemy", DefaultTargetEnemyTagID);
        foreach (var tag in _level.tags)
        {
            if (tag.tagID == targetTagID ||
                (targetCoreTagID > 0 && tag.tagID == targetCoreTagID) ||
                (targetEnemyTagID > 0 && tag.tagID == targetEnemyTagID))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    private void CacheTargetCells()
    {
        _targetCells.Clear();
        _coreTargetCells.Clear();

        if (_level?.tags == null)
            return;

        int targetTagID = ResolveTagID("target", DefaultTargetTagID);
        int targetCoreTagID = ResolveTagID("Target.Core", DefaultTargetCoreTagID);
        foreach (var tag in _level.tags)
        {
            if (tag.tagID == targetTagID)
                _targetCells.Add(new Vector2Int(tag.x, tag.y));
            else if (tag.tagID == targetCoreTagID)
                _coreTargetCells.Add(new Vector2Int(tag.x, tag.y));
        }
    }

    private int ResolveTagID(string tagName, int fallback)
    {
        if (_config == null || _config.tagDefinitions == null)
            return fallback;

        foreach (var tag in _config.tagDefinitions)
        {
            if (tag == null)
                continue;

            if (string.Equals(tag.tagName, tagName, System.StringComparison.OrdinalIgnoreCase))
                return tag.tagID;
        }

        return fallback;
    }

    private static bool IsEnemyTag(int tagId, params int[] enemyTagIds)
    {
        if (tagId <= 0 || enemyTagIds == null)
            return false;

        for (int i = 0; i < enemyTagIds.Length; i++)
        {
            if (enemyTagIds[i] > 0 && enemyTagIds[i] == tagId)
                return true;
        }

        return false;
    }

    private List<int> GetWallTerrainIds()
    {
        var result = new List<int>();
        if (_config == null || _config.entries == null)
            return result;

        foreach (var entry in _config.entries)
        {
            if (entry != null && entry.isWall)
                result.Add(entry.tileID);
        }

        return result;
    }

    private int GetDefaultFloorTerrainId()
    {
        if (_config == null || _config.entries == null)
            return 0;

        foreach (var entry in _config.entries)
        {
            if (entry != null && !entry.isWall && entry.tileID != 0)
                return entry.tileID;
        }

        return 0;
    }

    private interface ILevelPlayRule
    {
        bool ShouldStartSpawningOnBegin { get; }
        void Begin(LevelPlayer player, LevelPlayRequest request);
        void End(LevelPlayer player);
        void OnTick(LevelPlayer player);
        void Evaluate(LevelPlayer player);
    }

    private sealed class ClassicPlayRule : ILevelPlayRule
    {
        private enum Phase
        {
            PuzzleOnly,
            CombatUnlocked
        }

        private Phase _phase;
        private int _phaseOneBoxCount;
        private int _phaseOneGold;
        private int _phaseTwoGold;
        private int _convertedTargetCount;
        private readonly HashSet<int> _phaseOneSettledBoxIds = new();

        public bool ShouldStartSpawningOnBegin => false;

        public void Begin(LevelPlayer player, LevelPlayRequest request)
        {
            player.SetRemainingSteps(-1);
            _phase = Phase.PuzzleOnly;
            _phaseOneBoxCount = 0;
            _phaseOneGold = 0;
            _phaseTwoGold = 0;
            _convertedTargetCount = 0;
            _phaseOneSettledBoxIds.Clear();
            HandZone.SetCardsLocked(true);
        }

        public void OnTick(LevelPlayer player)
        {
        }

        public void End(LevelPlayer player)
        {
        }

        public void Evaluate(LevelPlayer player)
        {
            if (_phase != Phase.CombatUnlocked)
                return;

            if (!player.AreAllBoxesOnTargets() || player.HasAnyEnemyAlive())
                return;

            int phaseTwoBoxCount = player.CountBoxesOnTargetsExcluding(_phaseOneSettledBoxIds);
            _phaseTwoGold = player.AwardGoldByArithmeticSequence(phaseTwoBoxCount, 3, 1, "classic phase two boxes");
            player.SettleLevel(LevelPlayResult.Success, "classic phase two completed");
        }

        public void DrawGUI(LevelPlayer player)
        {
            DrawCasinoPanel(player);

            if (_phase == Phase.PuzzleOnly)
            {
                player.EnsureClassicPhaseStyles();
                int boxCount = player.CountBoxesOnTargets();
                int previewGold = CalculateArithmeticSequence(boxCount, 3, 3);
                string text = $"经典阶段一  箱子: {boxCount}  预期金币: {previewGold}";
                float width = Mathf.Min(560f, Screen.width - 32f);
                var rect = new Rect((Screen.width - width) * 0.5f, 18f, width, 42f);
                var shadowRect = new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height);
                GUI.Label(shadowRect, text, player._classicPhaseShadowStyle);
                GUI.Label(rect, text, player._classicPhaseStyle);

                var buttonRect = new Rect((Screen.width - 180f) * 0.5f, rect.yMax + 8f, 180f, 44f);
                if (GUI.Button(buttonRect, "结算"))
                    SettlePhaseOne(player);

                return;
            }

            player.EnsureClassicPhaseStyles();
            int newBoxes = player.CountBoxesOnTargetsExcluding(_phaseOneSettledBoxIds);
            int preview = CalculateArithmeticSequence(newBoxes, 3, 1);
            int enemyCount = player.CountEnemiesAlive();
            string combatText = $"经典阶段二  新箱子: {newBoxes}  预期金币: {preview}  怪物: {enemyCount}  魔化目标: {_convertedTargetCount}";
            float combatWidth = Mathf.Min(640f, Screen.width - 32f);
            var combatRect = new Rect((Screen.width - combatWidth) * 0.5f, 18f, combatWidth, 42f);
            var combatShadowRect = new Rect(combatRect.x + 2f, combatRect.y + 2f, combatRect.width, combatRect.height);
            GUI.Label(combatShadowRect, combatText, player._classicPhaseShadowStyle);
            GUI.Label(combatRect, combatText, player._classicPhaseStyle);
        }

        private void DrawCasinoPanel(LevelPlayer player)
        {
            player.EnsureCasinoStyles();

            float pulse = (Mathf.Sin(Time.time * 3.5f) + 1f) * 0.5f;
            Color goldPulse = new Color(1f, 0.85f + pulse * 0.15f, 0.05f + pulse * 0.35f, 1f);
            Color titlePulse = new Color(1f, 0.9f + pulse * 0.1f, 0.3f + pulse * 0.4f, 1f);

            int boxCount = player.CountBoxesOnTargets();
            int estimatedGold;
            string detailLine1;
            string detailLine2 = null;

            if (_phase == Phase.PuzzleOnly)
            {
                estimatedGold = CalculateArithmeticSequence(boxCount, 3, 3);
                detailLine1 = $"箱子 ×{boxCount}";
                if (boxCount > 0)
                    detailLine2 = BuildArithmeticString(boxCount, 3, 3) + $" = {estimatedGold} G";
            }
            else
            {
                int newBoxes = player.CountBoxesOnTargetsExcluding(_phaseOneSettledBoxIds);
                int phaseTwoEstimate = CalculateArithmeticSequence(newBoxes, 3, 1);
                estimatedGold = _phaseOneGold + phaseTwoEstimate;
                detailLine1 = $"阶段一 +{_phaseOneGold}G  |  阶段二 +{phaseTwoEstimate}G";
                if (newBoxes > 0)
                    detailLine2 = "新箱 " + BuildArithmeticString(newBoxes, 3, 1) + $" = {phaseTwoEstimate} G";
            }

            player._casinoGoldStyle.normal.textColor = goldPulse;
            player._casinoTitleStyle.normal.textColor = titlePulse;

            float panelW = 296f;
            float titleH = 28f;
            float goldH = 44f;
            float detailH = 20f;
            float pad = 10f;
            float panelH = detailLine2 != null
                ? pad + titleH + 6f + goldH + 4f + detailH + 4f + detailH + pad
                : pad + titleH + 6f + goldH + 4f + detailH + pad;
            float x = 16f;
            float y = 16f;

            var panelRect = new Rect(x, y, panelW, panelH);
            GUI.Box(panelRect, "", player._casinoPanelStyle);

            float cy = y + pad;

            var titleRect = new Rect(x + 12f, cy, panelW - 24f, titleH);
            var titleShadow = new Rect(titleRect.x + 1f, titleRect.y + 1f, titleRect.width, titleRect.height);
            GUI.Label(titleShadow, "♦  预计获得金币  ♦", player._casinoTitleShadowStyle);
            GUI.Label(titleRect, "♦  预计获得金币  ♦", player._casinoTitleStyle);
            cy += titleH + 6f;

            var goldRect = new Rect(x + 8f, cy, panelW - 16f, goldH);
            var goldShadow = new Rect(goldRect.x + 2f, goldRect.y + 2f, goldRect.width, goldRect.height);
            GUI.Label(goldShadow, $"★  {estimatedGold} G  ★", player._casinoGoldShadowStyle);
            GUI.Label(goldRect, $"★  {estimatedGold} G  ★", player._casinoGoldStyle);
            cy += goldH + 4f;

            var detail1Rect = new Rect(x + 16f, cy, panelW - 32f, detailH);
            GUI.Label(detail1Rect, detailLine1, player._casinoDetailStyle);
            cy += detailH + 4f;

            if (detailLine2 != null)
            {
                var detail2Rect = new Rect(x + 16f, cy, panelW - 32f, detailH);
                GUI.Label(detail2Rect, detailLine2, player._casinoDetailStyle);
            }
        }

        private static string BuildArithmeticString(int count, int first, int step)
        {
            if (count <= 0) return "0";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0) sb.Append(" + ");
                sb.Append(first + i * step);
            }
            return sb.ToString();
        }

        private void SettlePhaseOne(LevelPlayer player)
        {
            if (_phase != Phase.PuzzleOnly)
                return;

            _phaseOneBoxCount = player.CountBoxesOnTargets();
            player.CollectBoxIdsOnTargets(_phaseOneSettledBoxIds);
            _phaseOneGold = player.AwardGoldByArithmeticSequence(_phaseOneBoxCount, 3, 3, "classic phase one boxes");
            _convertedTargetCount = player.ConvertUnoccupiedClassicTargetsToEnemyTargets();
            _phase = Phase.CombatUnlocked;
            HandZone.SetCardsLocked(false);
            SpawnSystem.Instance?.StartSpawning();

            Debug.Log($"[LevelPlayer] Classic phase one settled: boxes={_phaseOneBoxCount}, gold={_phaseOneGold}, convertedTargets={_convertedTargetCount}");
            player._playRule.Evaluate(player);
        }

        private static int CalculateArithmeticSequence(int count, int firstAmount, int stepAmount)
        {
            if (count <= 0)
                return 0;

            int amount = 0;
            for (int i = 0; i < count; i++)
                amount += firstAmount + i * stepAmount;

            return amount;
        }
    }

    private sealed class StepLimitPlayRule : ILevelPlayRule
    {
        public bool ShouldStartSpawningOnBegin => true;

        public void Begin(LevelPlayer player, LevelPlayRequest request)
        {
            int stepLimit = request != null ? request.StepLimit : 0;
            player.SetRemainingSteps(Mathf.Max(1, stepLimit));
            HandZone.SetCardsLocked(false);
        }

        public void OnTick(LevelPlayer player)
        {
            player.SetRemainingSteps(Mathf.Max(0, player.RemainingSteps - 1));
        }

        public void End(LevelPlayer player)
        {
        }

        public void Evaluate(LevelPlayer player)
        {
            if (player.AreAllBoxesOnTargets())
            {
                player.SettleLevel(LevelPlayResult.Success, "all boxes are on targets");
                return;
            }

            if (player.RemainingSteps <= 0)
                player.SettleLevel(LevelPlayResult.Failure, "step limit reached");
        }
    }

    private sealed class EscortPlayRule : ILevelPlayRule
    {
        private LevelPlayer _player;
        private bool _completionRewardsGranted;

        public bool ShouldStartSpawningOnBegin => true;

        public void Begin(LevelPlayer player, LevelPlayRequest request)
        {
            _player = player;
            player.SetRemainingSteps(-1);
            _completionRewardsGranted = false;
            HandZone.SetCardsLocked(false);
            EventBusSystem.Instance?.On(StageEventType.EntityMoved, OnEntityMoved);
        }

        public void OnTick(LevelPlayer player)
        {
        }

        public void End(LevelPlayer player)
        {
            EventBusSystem.Instance?.Off(StageEventType.EntityMoved, OnEntityMoved);
            _player = null;
        }

        public void Evaluate(LevelPlayer player)
        {
            if (player.IsAnyCoreBoxOnTarget())
            {
                if (!_completionRewardsGranted)
                {
                    _completionRewardsGranted = true;
                    player.AwardEscortCompletionRewards();
                }

                player.SettleLevel(LevelPlayResult.Success, "core box reached a target");
                return;
            }

            if (!player.IsAnyCoreBoxAlive())
                player.SettleLevel(LevelPlayResult.Failure, "core box destroyed");
        }

        private void OnEntityMoved(StageEvent evt)
        {
            if (evt.EntityType != EntityType.Box)
                return;

            var player = _player;
            if (player == null || player._playRule != this || player._isSettled)
                return;

            player.TryConsumeEscortRewardBox(evt.Entity, evt.To);
        }
    }
}
