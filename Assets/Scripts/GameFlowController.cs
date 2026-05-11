using NekoGraph;
using UnityEngine;
using UnityEngine.Serialization;

public enum GameFlowMode
{
    DirectLevel,
    RouteMap,
    RoundFlow
}

public enum RouteMapStartupMode
{
    AutoStart,
    RunSetup
}

public class GameFlowController : MonoBehaviour
{
    public static GameFlowController Instance { get; private set; }

    [SerializeField] private GameFlowMode mode = GameFlowMode.DirectLevel;
    [SerializeField] private RunConfigSO runConfig;
    [SerializeField, FormerlySerializedAs("settings")] private RunRouteConfigSO routeSettings;
    [SerializeField] private RunStartSettings runStartSettings;
    [SerializeField] private bool ensureRouteOnGUI = true;
    [SerializeField] private RouteMapStartupMode routeMapStartupMode = RouteMapStartupMode.AutoStart;

    public GameFlowMode Mode => mode;
    public bool ShouldLevelPlayerAutoBuild => mode == GameFlowMode.DirectLevel;
    public bool IsInLevel { get; private set; }
    public RunConfigSO CurrentRunConfig => runConfig;
    public RunRouteConfigSO RouteSettings => runConfig != null && runConfig.routeSettings != null ? runConfig.routeSettings : routeSettings;
    public RunRoundConfigSO RoundSettings => runConfig != null ? runConfig.roundSettings : null;
    public RunDifficultyConfigSO DifficultySettings => runConfig != null ? runConfig.difficultySettings : null;
    public RunRewardConfigSO RewardSettings => runConfig != null ? runConfig.rewardSettings : null;
    public RunStartSettings RunStartSettings => runConfig != null && runConfig.startSettings != null ? runConfig.startSettings : runStartSettings;
    public float OverallDifficulty => DifficultySettings != null
        ? Mathf.Max(0f, DifficultySettings.overallDifficulty)
        : 1f;
    public EnemySpawnDifficultyProfileSO EnemySpawnDifficultyProfile => DifficultySettings != null
        ? DifficultySettings.enemySpawnDifficultyProfile
        : null;
    public int RouteLayerCount => RouteSettings != null ? RouteSettings.GetResolvedLayerCount() : 1;

    private RunRouteOnGUIFrontend _routeFrontend;
    private RunRoundController _roundController;
    private int _observedStageRunVersion;
    private float _routeNodeStartedAt;
    private bool _runStartApplied;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureFacades();
    }

    private void Start()
    {
        if (mode == GameFlowMode.DirectLevel)
        {
            EnsureFacades();
            ApplyRunStartSettings();
        }

        if (mode == GameFlowMode.RouteMap)
        {
            if (routeMapStartupMode == RouteMapStartupMode.AutoStart)
                InitializeRouteMap();
            else
                ShowRunSetup();
        }

        if (mode == GameFlowMode.RoundFlow)
            InitializeRoundFlow();
    }

    private void Update()
    {
        if (mode == GameFlowMode.DirectLevel && !_runStartApplied)
            ApplyRunStartSettings();

        if (mode == GameFlowMode.RouteMap)
            TryCompleteEncounterRouteNode();
        else if (mode == GameFlowMode.RoundFlow)
            TryCompleteRoundEventStage();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void EnsureFacades()
    {
        var hub = GraphHub.Instance;
        if (hub == null)
            return;

        if (hub.GetFacade<RunStageFacade>() == null)
            hub.RegisterFacade(new RunStageFacade());

        if (hub.GetFacade<RunRouteFacade>() == null)
            hub.RegisterFacade(new RunRouteFacade());

        if (hub.GetFacade<CardDeckFacade>() == null)
            hub.RegisterFacade(new CardDeckFacade());

        if (hub.GetFacade<RunInventoryFacade>() == null)
            hub.RegisterFacade(new RunInventoryFacade());

        if (hub.GetFacade<RunPlayerStatusFacade>() == null)
            hub.RegisterFacade(new RunPlayerStatusFacade());
    }

    public void InitializeRouteMap()
    {
        EnsureFacades();

        var routeFacade = GraphHub.Instance?.GetFacade<RunRouteFacade>();
        if (routeFacade == null)
            return;

        ApplyRunStartSettings();

        var routeSettings = RouteSettings;
        if (routeSettings != null)
        {
            routeFacade.GenerateRoute(routeSettings);
        }
        else
        {
            routeFacade.EnsureRoutePack();
        }

        if (FindObjectOfType<RunStageOnGUIFrontend>() == null)
        {
            gameObject.AddComponent<RunStageOnGUIFrontend>();
        }

        if (FindObjectOfType<RunShopOnGUIFrontend>() == null)
        {
            gameObject.AddComponent<RunShopOnGUIFrontend>();
        }

        if (ensureRouteOnGUI && FindObjectOfType<RunRouteOnGUIFrontend>() == null)
        {
            gameObject.AddComponent<RunRouteOnGUIFrontend>();
        }

        _routeFrontend = FindObjectOfType<RunRouteOnGUIFrontend>();
        SetRouteVisible(true);
    }

    private void ShowRunSetup()
    {
        EnsureFacades();

        if (FindObjectOfType<RunSetupOnGUIFrontend>() == null)
            gameObject.AddComponent<RunSetupOnGUIFrontend>();
    }

    public void StartRun(RunConfigSO config, GameFlowMode targetMode = GameFlowMode.RoundFlow)
    {
        runConfig = config;
        mode = targetMode;
        _runStartApplied = false;

        if (mode == GameFlowMode.RouteMap)
            InitializeRouteMap();
        else if (mode == GameFlowMode.RoundFlow)
            InitializeRoundFlow();
    }

    public void InitializeRoundFlow()
    {
        EnsureFacades();
        ApplyRunStartSettings();

        if (_routeFrontend != null)
            SetRouteVisible(false);

        if (_roundController == null)
            _roundController = FindObjectOfType<RunRoundController>();

        if (_roundController == null)
            _roundController = gameObject.AddComponent<RunRoundController>();

        if (FindObjectOfType<RunStageOnGUIFrontend>() == null)
            gameObject.AddComponent<RunStageOnGUIFrontend>();

        if (FindObjectOfType<RunShopOnGUIFrontend>() == null)
            gameObject.AddComponent<RunShopOnGUIFrontend>();

        _roundController.StartRun(runConfig, RoundSettings);
    }

    public void OnRouteClassicLevelStarted()
    {
        EnterLevel();
        _routeNodeStartedAt = Time.time;
        SetRouteVisible(false);
    }

    public void OnRouteClassicLevelCompleted()
    {
        OnRouteClassicLevelSettled(LevelPlayResult.Success);
    }

    public void OnRouteClassicLevelSettled(LevelPlayResult result)
    {
        ExitLevel();
        if (mode == GameFlowMode.RoundFlow)
        {
            if (_roundController == null)
                _roundController = FindObjectOfType<RunRoundController>();

            _roundController?.OnCombatSettled(result);
            return;
        }

        var routeFacade = GraphHub.Instance?.GetFacade<RunRouteFacade>();
        if (routeFacade != null && routeFacade.ActiveRouteNodeIsClassicLevel)
        {
            if (result == LevelPlayResult.Success)
                routeFacade.CompleteActiveRouteNode();
            else
                routeFacade.FailActiveRouteNode();
        }

        SetRouteVisible(true);
    }

    public void OnRouteEncounterStarted()
    {
        ExitLevel();
        _routeNodeStartedAt = Time.time;
        var stageFacade = GraphHub.Instance?.GetFacade<RunStageFacade>();
        _observedStageRunVersion = stageFacade?.LoadedStageRunVersion ?? 0;
        SetRouteVisible(false);
    }

    public void OnRoundEventStageStarted()
    {
        ExitLevel();
        _routeNodeStartedAt = Time.time;
        var stageFacade = GraphHub.Instance?.GetFacade<RunStageFacade>();
        _observedStageRunVersion = stageFacade?.LoadedStageRunVersion ?? 0;
    }

    public void EnterLevel()
    {
        IsInLevel = true;
    }

    public void ExitLevel()
    {
        IsInLevel = false;
    }

    private void ApplyRunStartSettings()
    {
        var startSettings = RunStartSettings;
        if (_runStartApplied || startSettings == null)
            return;

        var deck = GraphHub.Instance?.GetFacade<CardDeckFacade>();
        if (deck == null)
        {
            deck = new CardDeckFacade();
            GraphHub.Instance?.RegisterFacade(deck);
        }

        if (deck == null)
            return;

        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        if (inventory == null)
            return;

        var status = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        if (status == null)
        {
            status = new RunPlayerStatusFacade();
            GraphHub.Instance?.RegisterFacade(status);
        }

        if (status == null)
            return;

        if (deck.ReplaceWithStartingDeck(startSettings.startingDeck) &&
            inventory.Reset(startSettings.startingGold) &&
            status.Reset(startSettings.startingMaxHp, startSettings.startingHp))
        {
            _runStartApplied = true;
        }
    }

    public RunDifficultySnapshot BuildDifficultySnapshot(int routeLayer, int routeLayerCount)
    {
        if (DifficultySettings != null)
            return DifficultySettings.BuildSnapshot(routeLayer, routeLayerCount);

        var snapshot = RunDifficultySnapshot.Default;
        snapshot.Progress = RunDifficultyConfigSO.CalculateProgress(routeLayer, routeLayerCount);
        snapshot.OverallDifficulty = OverallDifficulty;
        snapshot.EnemySpawnDifficultyProfile = EnemySpawnDifficultyProfile;
        return snapshot;
    }

    private void TryCompleteEncounterRouteNode()
    {
        var routeFacade = GraphHub.Instance?.GetFacade<RunRouteFacade>();
        if (routeFacade == null || !routeFacade.IsRouteNodeRunning || routeFacade.ActiveRouteNodeIsClassicLevel)
            return;

        var stageFacade = GraphHub.Instance?.GetFacade<RunStageFacade>();
        if (stageFacade == null ||
            stageFacade.LoadedStageRunVersion != _observedStageRunVersion ||
            Time.time - _routeNodeStartedAt < 0.05f ||
            !stageFacade.IsLoadedStageComplete())
        {
            return;
        }

        stageFacade.ClearLoadedStage();
        routeFacade.CompleteActiveRouteNode();
        SetRouteVisible(true);
    }

    private void TryCompleteRoundEventStage()
    {
        if (_roundController == null)
            _roundController = FindObjectOfType<RunRoundController>();

        if (_roundController == null || _roundController.State != RunRoundState.EventStage)
            return;

        var stageFacade = GraphHub.Instance?.GetFacade<RunStageFacade>();
        if (stageFacade == null ||
            stageFacade.LoadedStageRunVersion != _observedStageRunVersion ||
            Time.time - _routeNodeStartedAt < 0.05f ||
            !stageFacade.IsLoadedStageComplete())
        {
            return;
        }

        stageFacade.ClearLoadedStage();
        _roundController.OnEventStageCompleted();
    }

    private void SetRouteVisible(bool isVisible)
    {
        if (_routeFrontend == null)
            _routeFrontend = FindObjectOfType<RunRouteOnGUIFrontend>();

        if (_routeFrontend != null)
        {
            if (!isVisible)
                _routeFrontend.CloseDeck();

            _routeFrontend.Visible = isVisible;
        }
    }
}
