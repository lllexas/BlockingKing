using NekoGraph;
using UnityEngine;

public enum GameFlowMode
{
    DirectLevel,
    RouteMap
}

public class GameFlowController : MonoBehaviour
{
    public static GameFlowController Instance { get; private set; }

    [SerializeField] private GameFlowMode mode = GameFlowMode.DirectLevel;
    [SerializeField] private GameFlowSettings settings;
    [SerializeField] private RunStartSettings runStartSettings;
    [SerializeField] private bool ensureRouteOnGUI = true;

    public GameFlowMode Mode => mode;
    public bool ShouldLevelPlayerAutoBuild => mode == GameFlowMode.DirectLevel;
    public bool IsInLevel { get; private set; }

    private RunRouteOnGUIFrontend _routeFrontend;
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
            InitializeRouteMap();
    }

    private void Update()
    {
        if (mode == GameFlowMode.DirectLevel && !_runStartApplied)
            ApplyRunStartSettings();

        if (mode != GameFlowMode.RouteMap)
            return;

        TryCompleteEncounterRouteNode();
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
    }

    public void InitializeRouteMap()
    {
        EnsureFacades();

        var routeFacade = GraphHub.Instance?.GetFacade<RunRouteFacade>();
        if (routeFacade == null)
            return;

        ApplyRunStartSettings();

        if (settings != null)
        {
            var sources = settings.BuildRouteStageSources();
            routeFacade.GenerateRoute(sources, settings.layerCount, settings.laneCount, settings.seed);
        }
        else
        {
            routeFacade.EnsureRoutePack();
        }

        if (FindObjectOfType<RunStageOnGUIFrontend>() == null)
        {
            gameObject.AddComponent<RunStageOnGUIFrontend>();
        }

        if (ensureRouteOnGUI && FindObjectOfType<RunRouteOnGUIFrontend>() == null)
        {
            gameObject.AddComponent<RunRouteOnGUIFrontend>();
        }

        _routeFrontend = FindObjectOfType<RunRouteOnGUIFrontend>();
        SetRouteVisible(true);
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

    public void EnterLevel()
    {
        IsInLevel = true;
    }

    public void ExitLevel()
    {
        IsInLevel = false;
    }

    public RunStartSettings RunStartSettings => runStartSettings;

    private void ApplyRunStartSettings()
    {
        if (_runStartApplied || runStartSettings == null)
            return;

        var deck = GraphHub.Instance?.GetFacade<CardDeckFacade>();
        if (deck == null)
        {
            deck = new CardDeckFacade();
            GraphHub.Instance?.RegisterFacade(deck);
        }

        if (deck == null)
            return;

        if (deck.ReplaceWithStartingDeck(runStartSettings.startingDeck))
            _runStartApplied = true;
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
