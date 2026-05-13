using NekoGraph;
using UnityEngine;
using UnityEngine.Serialization;

public enum GameFlowMode
{
    DirectLevel,
    RouteMap,
    RoundFlow,
    Tutorial,
    LevelEdit
}

public enum RouteMapStartupMode
{
    AutoStart,
    RunSetup,
    MainMenuRound
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
    [Header("Tutorial")]
    [SerializeField] private bool ensureTutorialDirector = true;

    public GameFlowMode Mode => mode;
    public bool ShouldLevelPlayerAutoBuild => mode == GameFlowMode.DirectLevel;
    public bool IsInLevel { get; private set; }
    public bool IsMainMenuVisible { get; private set; }
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
    private bool _isStartingRun;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (mode != GameFlowMode.LevelEdit)
        {
            EnsureFacades();
            EnsureBgmSystems();
        }
    }

    private void Start()
    {
        if (mode == GameFlowMode.LevelEdit)
        {
            StartLevelEdit();
            return;
        }

        if (routeMapStartupMode == RouteMapStartupMode.MainMenuRound)
        {
            ShowMainMenuRound(true);
            return;
        }

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

        if (mode == GameFlowMode.Tutorial)
            StartTutorialLevel();

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

    private void EnsureBgmSystems()
    {
        AudioBus.Ensure();

        var player = FindObjectOfType<BgmRecordPlayer>();
        if (player == null)
            player = gameObject.AddComponent<BgmRecordPlayer>();

        bool startsAtMainMenu = routeMapStartupMode == RouteMapStartupMode.MainMenuRound;
        player.PlayOnStart = !startsAtMainMenu;

        if (runConfig != null && runConfig.bgmPlaylist != null)
            player.Configure(runConfig.bgmPlaylist, !startsAtMainMenu);

        if (runConfig != null && runConfig.mainMenuBgm != null && startsAtMainMenu)
            player.PlayPrompt(runConfig.mainMenuBgm);

        if (FindObjectOfType<BgmRecordOnGUIFrontend>() == null)
            gameObject.AddComponent<BgmRecordOnGUIFrontend>();

        if (FindObjectOfType<RunSettingsOnGUIFrontend>() == null)
            gameObject.AddComponent<RunSettingsOnGUIFrontend>();
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
        if (_isStartingRun)
            return;

        _isStartingRun = true;
        runConfig = config;
        mode = targetMode;
        _runStartApplied = false;
        IsMainMenuVisible = false;

        HideMainMenu();
        PlayRunBgm();

        if (mode == GameFlowMode.RouteMap)
            InitializeRouteMap();
        else if (mode == GameFlowMode.RoundFlow)
            InitializeRoundFlow();
        else if (mode == GameFlowMode.Tutorial)
            StartTutorialLevel();
        else if (mode == GameFlowMode.LevelEdit)
            StartLevelEdit();

        _isStartingRun = false;
    }

    public void StartLevelEdit()
    {
#if UNITY_EDITOR
        mode = GameFlowMode.LevelEdit;
        IsMainMenuVisible = false;
        IsInLevel = false;
        HideMainMenu();
        HideRunUiPanels();
        HideBgmRecord();
        SetRouteVisible(false);
        HandZone.SetCardsLocked(true);

        var levelPlayer = LevelPlayer.ActiveInstance != null
            ? LevelPlayer.ActiveInstance
            : FindObjectOfType<LevelPlayer>();

        if (levelPlayer == null)
        {
            Debug.LogError("[GameFlowController] LevelEdit mode requires a LevelPlayer in the scene.");
            return;
        }

        var controller = levelPlayer.GetComponent<Level3DEditorController>();
        if (controller == null)
            controller = levelPlayer.gameObject.AddComponent<Level3DEditorController>();

        if (!levelPlayer.LoadConfiguredLevel())
        {
            Debug.LogError("[GameFlowController] LevelEdit mode requires LevelPlayer.levelData.");
            return;
        }

        controller.Configure(levelPlayer, levelPlayer.CurrentLevel, levelPlayer.CurrentConfig);
        Debug.Log($"[GameFlowController] LevelEdit started: {levelPlayer.CurrentLevel.name}");
#else
        Debug.LogWarning("[GameFlowController] LevelEdit mode is editor-only.");
#endif
    }

    public void StartTutorialLevel()
    {
        EnsureFacades();
        ApplyRunStartSettings();
        HideMainMenu();
        HideRunUiPanels();
        SetRouteVisible(false);
        IsMainMenuVisible = false;
        mode = GameFlowMode.Tutorial;

        var director = FindObjectOfType<TutorialStageDirector>();
        if (ensureTutorialDirector && director == null)
            director = gameObject.AddComponent<TutorialStageDirector>();

        if (director == null)
        {
            Debug.LogError("[GameFlowController] Tutorial mode requires a TutorialStageDirector.");
            ExitLevel();
            return;
        }

        director.StartTutorial();
    }

    public void StartRoundRunFromMainMenu()
    {
        if (runConfig == null)
        {
            Debug.LogError("[GameFlowController] Main menu cannot start RoundFlow because RunConfig is missing.");
            return;
        }

        StartRun(runConfig, GameFlowMode.RoundFlow);
    }

    public void ShowMainMenuRound(bool instant = false)
    {
        EnsureFacades();
        ExitLevel();
        _isStartingRun = false;
        IsMainMenuVisible = true;
        PlayMainMenuBgm();
        SetRouteVisible(false);
        HandZone.SetCardsLocked(true);
        HideRunUiPanels();
        ShowMainMenuPart(MainMenuUIIds.Backdrop, instant);
        ShowMainMenuPart(MainMenuUIIds.Title, instant);
        ShowMainMenuPart(MainMenuUIIds.Start, instant);
        ShowMainMenuPart(MainMenuUIIds.Settings, instant);
        ShowMainMenuPart(MainMenuUIIds.Quit, instant);
    }

    public void ReturnToMainMenuRound()
    {
        if (IsMainMenuVisible)
        {
            PlayMainMenuBgm();
            HideSettingsPanel();
            return;
        }

        mode = GameFlowMode.RoundFlow;
        IsMainMenuVisible = true;
        IsInLevel = false;
        HandZone.SetCardsLocked(true);
        LevelPlayer.ActiveInstance?.StopPlayback();
        PlayMainMenuBgm();

        if (_routeFrontend != null)
            _routeFrontend.CloseDeck();

        _roundController = FindObjectOfType<RunRoundController>();
        HideRunUiPanels();
        ShowMainMenuRound(false);
    }

    public void InitializeRoundFlow()
    {
        EnsureFacades();
        ApplyRunStartSettings();

        _routeFrontend = FindObjectOfType<RunRouteOnGUIFrontend>();
        if (_routeFrontend == null)
            _routeFrontend = gameObject.AddComponent<RunRouteOnGUIFrontend>();
        _routeFrontend.Visible = true;
        _routeFrontend.CloseDeck();
        HandZone.SetCardsLocked(true);

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

    private void PlayMainMenuBgm()
    {
        if (runConfig == null || runConfig.mainMenuBgm == null)
            return;

        var player = FindObjectOfType<BgmRecordPlayer>();
        player?.PlayPrompt(runConfig.mainMenuBgm);
    }

    private void PlayRunBgm()
    {
        if (runConfig == null || runConfig.bgmPlaylist == null)
            return;

        var player = FindObjectOfType<BgmRecordPlayer>();
        if (player == null)
            return;

        player.Configure(runConfig.bgmPlaylist, false);
        player.PlayDefault();
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

    public void OnTutorialLevelSettled(LevelPlayResult result)
    {
        ExitLevel();
        Debug.Log($"[GameFlowController] Tutorial settled: {result}");
    }

    public void EnterLevel()
    {
        IsInLevel = true;
        HandZone.SetCardsLocked(false);
    }

    public void ExitLevel()
    {
        IsInLevel = false;
        HandZone.SetCardsLocked(true);
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

    private static void HideMainMenu()
    {
        HideMainMenuPart(MainMenuUIIds.Backdrop);
        HideMainMenuPart(MainMenuUIIds.Title);
        HideMainMenuPart(MainMenuUIIds.Start);
        HideMainMenuPart(MainMenuUIIds.Settings);
        HideMainMenuPart(MainMenuUIIds.Quit);
    }

    private static void HideRunUiPanels()
    {
        HideRunRoundPart(RunRoundUIIds.Backdrop);
        HideRunRoundPart(RunRoundUIIds.Hud);
        HideRunRoundPart(RunRoundUIIds.ClassicChoice);
        HideRunRoundPart(RunRoundUIIds.EscortChoice);
        HideRunRoundPart(RunRoundUIIds.SkipChoice);
        HideRunRoundPart(RunRoundUIIds.ShopChoice);
        HideRunRoundPart(RunRoundUIIds.EventChoice);
        HideRunRoundPart(RunRoundUIIds.CombatSettlement);
        HideRunRoundPart(RunRoundUIIds.Result);
        HideSettingsPanel();
        HideBgmRecord();
        HideHandPilePanel(HandPileUIIds.DrawPile);
        HideHandPilePanel(HandPileUIIds.DiscardPile);
    }

    private static void HideBgmRecord()
    {
        PostSystem.Instance?.Send("期望隐藏面板", new BgmRecordUIRequest(BgmRecordUIIds.RecordButton));
    }

    private static void HideRunRoundPart(string uiid)
    {
        PostSystem.Instance?.Send("期望隐藏面板", uiid);
    }

    private static void HideSettingsPanel()
    {
        PostSystem.Instance?.Send("期望隐藏面板", new RunSettingsPanelUIRequest());
    }

    private static void HideHandPilePanel(string uiid)
    {
        PostSystem.Instance?.Send("期望隐藏面板", new HandPileUIRequest(uiid, 0));
    }

    private void ShowMainMenuPart(string uiid, bool instant)
    {
        PostSystem.Instance?.Send("期望显示面板", new MainMenuUIRequest(uiid)
        {
            Controller = this,
            RunConfig = runConfig,
            Instant = instant
        });
    }

    private static void HideMainMenuPart(string uiid)
    {
        PostSystem.Instance?.Send("期望隐藏面板", new MainMenuUIRequest(uiid));
    }
}
