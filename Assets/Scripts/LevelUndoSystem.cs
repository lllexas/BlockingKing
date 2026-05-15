using System.Collections.Generic;
using NekoGraph;
using UnityEngine;

public sealed class LevelUndoSystem : MonoBehaviour
{
    public static LevelUndoSystem Instance { get; private set; }

    [SerializeField] private bool debugLog = true;

    private readonly Stack<LevelUndoSnapshot> _history = new();
    private bool _enabledForLevel;
    private bool _isRestoring;
    private bool _captureSuppressed;
    private int _paidUndoCount;
    private GUIStyle _labelStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _disabledButtonStyle;
    private GUIStyle _panelStyle;

    public int Count => _history.Count;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnGUI()
    {
        if (!_enabledForLevel)
            return;

        DrawUndoControls();
    }

    public void BeginLevel()
    {
        _history.Clear();
        _paidUndoCount = 0;
        _enabledForLevel = true;
    }

    public void EndLevel()
    {
        _history.Clear();
        _enabledForLevel = false;
        _isRestoring = false;
        _captureSuppressed = false;
        _paidUndoCount = 0;
    }

    public void SetCaptureSuppressed(bool suppressed)
    {
        _captureSuppressed = suppressed;
    }

    public LevelUndoSnapshot CaptureRuntimeSnapshot(IntentType intentType = IntentType.Noop)
    {
        return CaptureSnapshot(intentType);
    }

    public void RestoreRuntimeSnapshot(LevelUndoSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        _isRestoring = true;
        try
        {
            RestoreSnapshot(snapshot);
        }
        finally
        {
            _isRestoring = false;
        }
    }

    public void ClearHistory(string reason = null)
    {
        int count = _history.Count;
        _history.Clear();

        if (debugLog)
            Debug.Log($"[LevelUndoSystem] History cleared: count={count}, reason={reason ?? "unspecified"}");
    }

    public static void CaptureBeforePlayerIntent(IntentType intentType)
    {
        var instance = Instance;
        if (instance == null)
            return;

        instance.CaptureBeforePlayerIntentInternal(intentType);
    }

    private void CaptureBeforePlayerIntentInternal(IntentType intentType)
    {
        if (!_enabledForLevel || _isRestoring || _captureSuppressed)
            return;

        if (!CanCapture())
            return;

        var snapshot = CaptureSnapshot(intentType);
        if (snapshot == null)
            return;

        _history.Push(snapshot);
        if (debugLog)
            Debug.Log($"[LevelUndoSystem] Snapshot pushed: intent={intentType}, count={_history.Count}");
    }

    public bool TryUndoWithGold()
    {
        int cost = GetGoldCost();
        if (!CanSpendGold(cost))
        {
            if (debugLog)
                Debug.Log($"[LevelUndoSystem] Gold undo ignored: cost={cost}, gold={GetGold()}.");
            return false;
        }

        if (!TryUndo(UndoPaymentKind.Gold, cost))
            return false;

        return true;
    }

    public bool TryUndoWithHealth()
    {
        int cost = GetHealthCost();
        if (!CanSpendHealth(cost))
        {
            if (debugLog)
                Debug.Log($"[LevelUndoSystem] Health undo ignored: cost={cost}, hp={GetCurrentPlayerHealth()}.");
            return false;
        }

        if (!TryUndo(UndoPaymentKind.Health, cost))
            return false;

        return true;
    }

    private bool TryUndo(UndoPaymentKind paymentKind, int cost)
    {
        if (!_enabledForLevel || _isRestoring)
            return false;

        if (_history.Count == 0)
        {
            if (debugLog)
                Debug.Log("[LevelUndoSystem] Undo ignored: history is empty.");
            return false;
        }

        if (!CanRestore())
        {
            if (debugLog)
                Debug.Log("[LevelUndoSystem] Undo ignored: presentation or intent system is busy.");
            return false;
        }

        var snapshot = _history.Pop();
        _isRestoring = true;
        try
        {
            RestoreSnapshot(snapshot);
            if (!ApplyPayment(paymentKind, cost))
            {
                Debug.LogWarning($"[LevelUndoSystem] Undo restored but payment failed: kind={paymentKind}, cost={cost}");
                return false;
            }

            _paidUndoCount++;
        }
        finally
        {
            _isRestoring = false;
        }

        if (debugLog)
            Debug.Log($"[LevelUndoSystem] Undo restored: remaining={_history.Count}");

        return true;
    }

    private void DrawUndoControls()
    {
        EnsureStyles();

        int goldCost = GetGoldCost();
        int healthCost = GetHealthCost();
        bool canRestore = _history.Count > 0 && CanRestore();
        bool canGold = canRestore && CanSpendGold(goldCost);
        bool canHealth = canRestore && CanSpendHealth(healthCost);

        float width = 96f;
        float height = 116f;
        var rect = new Rect(Screen.width - width - 16f, (Screen.height - height) * 0.5f, width, height);
        GUI.Box(rect, GUIContent.none, _panelStyle);

        var labelRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 24f);
        GUI.Label(labelRect, "悔棋", _labelStyle);

        var goldRect = new Rect(rect.x + 8f, rect.y + 38f, rect.width - 16f, 30f);
        var healthRect = new Rect(rect.x + 8f, rect.y + 74f, rect.width - 16f, 30f);

        GUI.enabled = canGold;
        if (GUI.Button(goldRect, $"金币 {goldCost}", canGold ? _buttonStyle : _disabledButtonStyle))
            TryUndoWithGold();

        GUI.enabled = canHealth;
        if (GUI.Button(healthRect, $"血量 {healthCost}", canHealth ? _buttonStyle : _disabledButtonStyle))
            TryUndoWithHealth();

        GUI.enabled = true;
    }

    private void EnsureStyles()
    {
        if (_labelStyle != null)
            return;

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 17,
            fontStyle = FontStyle.Bold
        };
        _labelStyle.normal.textColor = new Color(1f, 0.93f, 0.55f, 1f);

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold
        };

        _disabledButtonStyle = new GUIStyle(_buttonStyle);
        _panelStyle = new GUIStyle(GUI.skin.box);
    }

    private int GetGoldCost()
    {
        int step = _paidUndoCount + 1;
        return step * step;
    }

    private int GetHealthCost()
    {
        return _paidUndoCount * 2 + 1;
    }

    private static bool ApplyPayment(UndoPaymentKind kind, int cost)
    {
        return kind switch
        {
            UndoPaymentKind.Gold => SpendGold(cost),
            UndoPaymentKind.Health => SpendHealth(cost),
            _ => false
        };
    }

    private static int GetGold()
    {
        return GraphHub.Instance?.GetFacade<RunInventoryFacade>()?.Gold ?? 0;
    }

    private static bool CanSpendGold(int cost)
    {
        return cost > 0 && GetGold() >= cost;
    }

    private static bool SpendGold(int cost)
    {
        if (cost <= 0)
            return false;

        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        return inventory != null && inventory.TrySpendGold(cost);
    }

    private static int GetCurrentPlayerHealth()
    {
        var entitySystem = EntitySystem.Instance;
        if (!TryFindPlayerIndex(entitySystem, out int playerIndex))
            return 0;

        return CombatStats.GetCurrentHealth(entitySystem.entities.statusComponents[playerIndex]);
    }

    private static bool CanSpendHealth(int cost)
    {
        return cost > 0 && GetCurrentPlayerHealth() > cost;
    }

    private static bool SpendHealth(int cost)
    {
        if (cost <= 0)
            return false;

        var entitySystem = EntitySystem.Instance;
        if (!TryFindPlayerIndex(entitySystem, out int playerIndex))
            return false;

        ref var status = ref entitySystem.entities.statusComponents[playerIndex];
        int currentHp = CombatStats.GetCurrentHealth(status);
        if (currentHp <= cost)
            return false;

        CombatStats.DealDamage(ref status, cost);
        int nextHp = CombatStats.GetCurrentHealth(status);
        int maxHp = CombatStats.GetMaxHealth(status);
        ApplyPlayerHealthToCoreBoxes(entitySystem, maxHp, nextHp);

        var statusFacade = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        if (statusFacade == null)
        {
            statusFacade = new RunPlayerStatusFacade();
            GraphHub.Instance?.RegisterFacade(statusFacade);
        }

        statusFacade?.SetHp(nextHp, maxHp);
        return true;
    }

    private static bool TryFindPlayerIndex(EntitySystem entitySystem, out int index)
    {
        index = -1;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        for (int i = 0; i < entitySystem.entities.entityCount; i++)
        {
            if (entitySystem.entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            index = i;
            return true;
        }

        return false;
    }

    private static void ApplyPlayerHealthToCoreBoxes(EntitySystem entitySystem, int maxHp, int currentHp)
    {
        if (entitySystem?.entities == null)
            return;

        var entities = entitySystem.entities;
        maxHp = Mathf.Max(1, maxHp);
        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Box || !entities.propertyComponents[i].IsCore)
                continue;

            ref var status = ref entities.statusComponents[i];
            status.BaseMaxHealth = maxHp;
            status.MaxHealthModifier = 0;
            status.DamageTaken = Mathf.Max(0, maxHp - currentHp);
        }
    }

    private static bool CanCapture()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        var intentSystem = IntentSystem.Instance;
        return intentSystem == null || !intentSystem.IsRunning;
    }

    private static bool CanRestore()
    {
        var intentSystem = IntentSystem.Instance;
        if (intentSystem != null && !intentSystem.CanRestoreLevelUndo)
            return false;

        var drawSystem = DrawSystem.Instance;
        if (drawSystem != null && drawSystem.IsBeatMotionBusy)
            return false;

        return !LevelPlayer.IsActiveStageInputLocked;
    }

    private static LevelUndoSnapshot CaptureSnapshot(IntentType intentType)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized)
            return null;

        return new LevelUndoSnapshot
        {
            IntentType = intentType,
            EntitySnapshot = entitySystem.CaptureSnapshot(),
            StatusEffectSnapshot = StatusEffectSystem.Instance != null
                ? StatusEffectSystem.Instance.CaptureSnapshot()
                : null,
            HandSnapshot = HandZone.ActiveInstance != null
                ? HandZone.ActiveInstance.CaptureSnapshot()
                : null,
            LevelSnapshot = LevelPlayer.ActiveInstance != null
                ? LevelPlayer.ActiveInstance.CaptureUndoSnapshot()
                : null
        };
    }

    private static void RestoreSnapshot(LevelUndoSnapshot snapshot)
    {
        IntentSystem.Instance?.Clear();
        GridOverlayDrawSystem.Instance?.ClearAll();

        if (snapshot.EntitySnapshot != null)
            EntitySystem.Instance?.RestoreSnapshot(snapshot.EntitySnapshot);

        StatusEffectSystem.Instance?.RestoreSnapshot(snapshot.StatusEffectSnapshot);

        DrawSystem.Instance?.ClearPresentationState();
        TerrainDrawSystem terrainDrawSystem = TerrainDrawSystemFromLevelPlayer();
        terrainDrawSystem?.MarkDirty();

        if (snapshot.HandSnapshot != null && HandZone.ActiveInstance != null)
            HandZone.ActiveInstance.RestoreSnapshot(snapshot.HandSnapshot);

        if (snapshot.LevelSnapshot != null && LevelPlayer.ActiveInstance != null)
            LevelPlayer.ActiveInstance.RestoreUndoSnapshot(snapshot.LevelSnapshot);

        IntentSystem.Instance?.ResolveWorldState();
    }

    private static TerrainDrawSystem TerrainDrawSystemFromLevelPlayer()
    {
        var player = LevelPlayer.ActiveInstance;
        return player != null ? player.GetComponent<TerrainDrawSystem>() : Object.FindObjectOfType<TerrainDrawSystem>();
    }

    public sealed class LevelUndoSnapshot
    {
        public IntentType IntentType;
        public EntitySystemSnapshot EntitySnapshot;
        public List<StatusEffectState> StatusEffectSnapshot;
        public HandZoneSnapshot HandSnapshot;
        public LevelPlayerUndoSnapshot LevelSnapshot;
    }

    private enum UndoPaymentKind
    {
        Gold,
        Health
    }
}
