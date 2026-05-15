using NekoGraph;
using UnityEngine;
using UnityEngine.Serialization;

public enum GameFlowMode
{
    DirectLevel,
    RouteMap,
    RoundFlow,
    LinearCampaign,
    Tutorial,
    LevelEdit,
    Solver
}

public enum GameFlowStartupMode
{
    DirectInGame,
    MainMenu
}

public class GameFlowController : MonoBehaviour
{
    public static GameFlowController Instance { get; private set; }

    [SerializeField] private GameFlowMode mode = GameFlowMode.DirectLevel;
    [SerializeField] private GameFlowStartupMode startupMode = GameFlowStartupMode.DirectInGame;
    [SerializeField] private RunConfigSO runConfig;
    [SerializeField] private LinearCampaignConfigSO linearCampaignConfig;
    [SerializeField, FormerlySerializedAs("settings")] private RunRouteConfigSO routeSettings;
    [SerializeField] private RunStartSettings runStartSettings;
    [SerializeField] private bool ensureRouteOnGUI = true;
    [Header("Tutorial")]
    [SerializeField] private bool ensureTutorialDirector = true;

    public GameFlowMode Mode => mode;
    public GameFlowStartupMode StartupMode => startupMode;
    public bool ShouldLevelPlayerAutoBuild => mode == GameFlowMode.DirectLevel;
    public bool IsInLevel { get; private set; }
    public bool IsMainMenuVisible { get; private set; }
    public RunConfigSO CurrentRunConfig => runConfig;
    public LinearCampaignConfigSO LinearCampaignConfig => linearCampaignConfig;
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
    private LinearModeDirector _linearDirector;
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

        EnsureFacades();
        EnsureBgmSystems();
    }

    private void Start()
    {
        var solverSession = Resources.Load<LevelSolverSession>("LevelSolverSession");
        if (solverSession != null && solverSession.active)
            mode = GameFlowMode.Solver;

        if (mode == GameFlowMode.LevelEdit)
        {
            StartLevelEdit();
            return;
        }

        if (startupMode == GameFlowStartupMode.MainMenu)
        {
            ShowMainMenu(true);
            return;
        }

        IsMainMenuVisible = false;
        HideMainMenu(true);
        EnterCurrentMode();
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

        bool startsAtMainMenu = startupMode == GameFlowStartupMode.MainMenu;
        bool startsInLevelEdit = mode == GameFlowMode.LevelEdit;
        bool shouldAutoPlay = !startsAtMainMenu && !startsInLevelEdit;
        player.PlayOnStart = shouldAutoPlay;

        if (runConfig != null && runConfig.bgmPlaylist != null)
            player.Configure(runConfig.bgmPlaylist, shouldAutoPlay);

        if (runConfig != null && runConfig.mainMenuBgm != null && startsAtMainMenu && !startsInLevelEdit)
            player.PlayPrompt(runConfig.mainMenuBgm);

        if (FindObjectOfType<BgmRecordOnGUIFrontend>() == null)
            gameObject.AddComponent<BgmRecordOnGUIFrontend>();

        if (FindObjectOfType<RunSettingsOnGUIFrontend>() == null)
            gameObject.AddComponent<RunSettingsOnGUIFrontend>();

        if (FindObjectOfType<LinearCampaignOnGUIFrontend>() == null)
            gameObject.AddComponent<LinearCampaignOnGUIFrontend>();
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

    public void SetMode(GameFlowMode targetMode)
    {
        mode = targetMode;
    }

    public void EnterCurrentMode()
    {
        switch (mode)
        {
            case GameFlowMode.DirectLevel:
                EnsureFacades();
                ApplyRunStartSettings();
                break;

            case GameFlowMode.RouteMap:
                InitializeRouteMap();
                break;

            case GameFlowMode.RoundFlow:
                InitializeRoundFlow();
                break;

            case GameFlowMode.LinearCampaign:
                StartLinearCampaign();
                break;

            case GameFlowMode.Tutorial:
                StartTutorialLevel();
                break;

            case GameFlowMode.LevelEdit:
                StartLevelEdit();
                break;

            case GameFlowMode.Solver:
                StartSolver();
                break;
        }
    }

    public void StartSolver()
    {
        EnsureFacades();
        HideMainMenu();
        HideRunUiPanels();
        SetRouteVisible(false);
        IsMainMenuVisible = false;
        IsInLevel = true;
        mode = GameFlowMode.Solver;
        HandZone.SetCardsLocked(false);

        var solver = FindObjectOfType<RuntimeLevelSolver>();
        if (solver == null)
            solver = gameObject.AddComponent<RuntimeLevelSolver>();

        solver.StartFromSession(Resources.Load<LevelSolverSession>("LevelSolverSession"));
    }

    public void StartModeFromMainMenu(GameFlowMode targetMode)
    {
        if (_isStartingRun)
            return;

        _isStartingRun = true;
        mode = targetMode;
        IsMainMenuVisible = false;

        PrepareToEnterMode(targetMode);
        if (targetMode != GameFlowMode.LevelEdit)
            PlayRunBgm();
        EnterCurrentMode();

        _isStartingRun = false;
    }

    public void StartRun(RunConfigSO config, GameFlowMode targetMode = GameFlowMode.RoundFlow)
    {
        runConfig = config;
        _runStartApplied = false;
        StartModeFromMainMenu(targetMode);
    }

    public void StartLevelEdit()
    {
#if UNITY_EDITOR
        mode = GameFlowMode.LevelEdit;
        IsMainMenuVisible = false;
        IsInLevel = false;
        HideMainMenu();
        HideRunUiPanels();
        SetRouteVisible(false);
        HandZone.SetCardsLocked(true);
        HideBgmRecord();

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
        ResetTutorialGold();
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

    private static void ResetTutorialGold()
    {
        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        inventory?.SetGold(0);
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

    public void StartCurrentModeFromMainMenu()
    {
        if (mode == GameFlowMode.RoundFlow)
        {
            StartRoundRunFromMainMenu();
            return;
        }

        if (mode == GameFlowMode.RouteMap && runConfig != null)
        {
            StartRun(runConfig, GameFlowMode.RouteMap);
            return;
        }

        StartModeFromMainMenu(mode);
    }

    public void StartTutorialFromMainMenu()
    {
        StartModeFromMainMenu(GameFlowMode.Tutorial);
    }

    public void StartLinearCampaignFromMainMenu()
    {
        StartModeFromMainMenu(GameFlowMode.LinearCampaign);
    }

    private void PrepareToEnterMode(GameFlowMode targetMode)
    {
        StopTutorialDirector();
        StopLinearDirector();
        HideMainMenu();
        HideRunUiPanels();
        SetRouteVisible(false);
        IsMainMenuVisible = false;
        IsInLevel = false;
        HandZone.SetCardsLocked(true);
    }

    public void ShowMainMenu(bool instant = false)
    {
        EnsureFacades();
        StopTutorialDirector();
        StopLinearDirector();
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
        ShowMainMenuPart(MainMenuUIIds.Campaign, instant);
        ShowMainMenuPart(MainMenuUIIds.Tutorial, instant);
        ShowMainMenuPart(MainMenuUIIds.Settings, instant);
        ShowMainMenuPart(MainMenuUIIds.Quit, instant);
    }

    [System.Obsolete("Use ShowMainMenu. This wrapper is kept for old UI and scene references.")]
    public void ShowMainMenuRound(bool instant = false)
    {
        ShowMainMenu(instant);
    }

    public void ReturnToMainMenu()
    {
        if (IsMainMenuVisible)
        {
            StopTutorialDirector();
            PlayMainMenuBgm();
            HideSettingsPanel();
            return;
        }

        StopTutorialDirector();
        StopLinearDirector();
        IsMainMenuVisible = true;
        IsInLevel = false;
        HandZone.SetCardsLocked(true);
        LevelPlayer.ActiveInstance?.StopPlayback();
        PlayMainMenuBgm();

        if (_routeFrontend != null)
            _routeFrontend.CloseDeck();

        _roundController = FindObjectOfType<RunRoundController>();
        HideRunUiPanels();
        ShowMainMenu(false);
    }

    [System.Obsolete("Use ReturnToMainMenu. This wrapper is kept for old UI and scene references.")]
    public void ReturnToMainMenuRound()
    {
        ReturnToMainMenu();
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

    public void StartLinearCampaign()
    {
        EnsureFacades();
        ApplyRunStartSettings();
        HideMainMenu();
        HideRunUiPanels();
        SetRouteVisible(false);
        HandZone.SetCardsLocked(true);

        if (linearCampaignConfig == null)
        {
            Debug.LogError("[GameFlowController] LinearCampaign mode requires a LinearCampaignConfigSO.");
            ReturnToMainMenu();
            return;
        }

        PlayRunBgm();

        if (_linearDirector == null)
            _linearDirector = FindObjectOfType<LinearModeDirector>();

        if (_linearDirector == null)
            _linearDirector = gameObject.AddComponent<LinearModeDirector>();

        _linearDirector.Configure(linearCampaignConfig);
        _linearDirector.StartCampaign();
        LinearCampaignOnGUIFrontend.Instance?.Show();
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
        ReturnToMainMenu();
    }

    public void OnLinearLevelSettled(LevelPlayResult result)
    {
        ExitLevel();
        _linearDirector ??= FindObjectOfType<LinearModeDirector>();
        if (_linearDirector == null)
        {
            Debug.LogError($"[GameFlowController] Linear level settled without LinearModeDirector: {result}");
            ReturnToMainMenu();
            return;
        }

        _linearDirector.OnLevelSettled(result);
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
        ApplyStartSettings(RunStartSettings);
    }

    private void ApplyStartSettings(RunStartSettings startSettings)
    {
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

    private static void HideMainMenu(bool instant = false)
    {
        HideMainMenuPart(MainMenuUIIds.Backdrop, instant);
        HideMainMenuPart(MainMenuUIIds.Title, instant);
        HideMainMenuPart(MainMenuUIIds.Start, instant);
        HideMainMenuPart(MainMenuUIIds.Campaign, instant);
        HideMainMenuPart(MainMenuUIIds.Tutorial, instant);
        HideMainMenuPart(MainMenuUIIds.Settings, instant);
        HideMainMenuPart(MainMenuUIIds.Quit, instant);
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

    private static void ShowBgmRecord()
    {
        PostSystem.Instance?.Send("期望显示面板", new BgmRecordUIRequest(BgmRecordUIIds.RecordButton));
    }

    private static void StopTutorialDirector()
    {
        var director = FindObjectOfType<TutorialStageDirector>();
        director?.StopTutorial();
    }

    private static void StopLinearDirector()
    {
        var director = FindObjectOfType<LinearModeDirector>();
        director?.StopCampaign();
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

    private static void HideMainMenuPart(string uiid, bool instant = false)
    {
        PostSystem.Instance?.Send("期望隐藏面板", new MainMenuUIRequest(uiid)
        {
            Instant = instant
        });
    }
}
