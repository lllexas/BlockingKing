using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 监听玩家输入，写入玩家单位 Intent，并推进 Tick。
/// </summary>
public class UserInputReader : MonoBehaviour
{
    private const int MoveDistance = 1;
    private const string ActivePathPreviewOverlayId = "player_path_preview_active";
    private const string LeftPathHoverOverlayId = "player_path_hover_left";
    private const string RightPathHoverOverlayId = "player_path_hover_right";

    [Header("Move Input")]
    [SerializeField, Min(0f)] private float movePreInputWindow = 0.15f;
    [SerializeField] private Color leftPathHoverColor = new(0.2f, 0.85f, 1f, 0.18f);
    [SerializeField] private Color rightPathHoverColor = new(1f, 0.72f, 0.25f, 0.16f);
    [SerializeField] private Color leftActivePathColor = new(0.25f, 0.85f, 1f, 0.42f);
    [SerializeField] private Color rightActivePathColor = new(1f, 0.72f, 0.25f, 0.38f);
    [SerializeField, Min(0f)] private float pathPreviewHeight = 0.014f;
    [SerializeField] private int pathPreviewPriority = 5;
    [SerializeField, Range(2, 128)] private int pathMemoryMaxCells = 96;
    [SerializeField, Range(1, 96)] private int pathMemoryMaxWaypoints = 48;
    [SerializeField, Min(0)] private int pathMemoryConnectMaxExtraSteps = 8;
    [SerializeField, Range(1f, 4f)] private float pathMemoryConnectMaxDetourRatio = 2.5f;

    private EntityHandle _playerHandle = EntityHandle.None;
    private Vector2Int _bufferedMoveDirection;
    private float _bufferedMoveExpireTime;
    private PathMoveMode _pathMoveMode;
    private readonly Queue<Vector2Int> _pathMoveDirections = new();
    private readonly Queue<Vector2Int> _leftPathHoverDirections = new();
    private readonly Queue<Vector2Int> _rightPathHoverDirections = new();
    private readonly Queue<Vector2Int> _pathMemoryConnectDirections = new();
    private readonly List<GridPathFlowOverlayCell> _pathPreviewCells = new();
    private readonly List<int> _pathOpenSet = new();
    private readonly List<Vector2Int> _mousePathMemoryCells = new();
    private bool _hasPathHoverCache;
    private Vector2Int _cachedPathHoverStart;
    private Vector2Int _cachedPathHoverTarget;
    private bool _hasMousePathMemoryStart;
    private Vector2Int _mousePathMemoryStart;
    private bool _isLeftPathDragging;

    private static readonly Vector2Int[] CrossDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private enum PathMoveMode
    {
        None,
        TerrainOnly,
        TerrainAndEntities
    }

    private void Update()
    {
        TryConsumeBufferedMove();
        TryConsumePathMove();

        bool hasMoveKey = TryReadMoveDirection(out var direction);
        if (hasMoveKey && HandZone.IsAnyCardAiming)
            HandZone.TryCancelActivePendingCard();
        if (hasMoveKey && HandZone.HasAssistSelection)
            HandZone.ClearAssistSelectionActive();

        if (IsPointerBlockedForStageInputExceptAssist())
        {
            if (_isLeftPathDragging)
                CancelLeftPathDrag();

            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (TryHandleCardAssistClick())
                return;

            if (HandZone.HasAssistSelection)
                HandZone.ClearAssistSelectionActive();

            TryBeginLeftPathDrag();
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            if (_isLeftPathDragging)
            {
                CancelLeftPathDrag();
                return;
            }

            if (HandZone.HasAssistSelection)
                HandZone.ClearAssistSelectionActive();

            TryStartPathMove(PathMoveMode.TerrainAndEntities);
            return;
        }

        if (Input.GetMouseButtonUp(0) && _isLeftPathDragging)
        {
            TryCommitLeftPathDrag();
            return;
        }

        if (!hasMoveKey)
            return;

        ClearPathMove();
        ClearMousePathMemory();
        _isLeftPathDragging = false;

        if (TryBufferMoveDuringBeatMotion(direction))
            return;

        TrySubmitMove(direction);
    }

    private void LateUpdate()
    {
        UpdatePathPreview();
    }

    private void TryConsumePathMove()
    {
        if (_pathMoveDirections.Count == 0)
            return;

        if (HandZone.IsAnyCardInteractionActive)
        {
            ClearPathPreview();
            return;
        }

        if (IsBeatMotionBusy())
            return;

        if (!TryResolvePlayer(out var playerHandle))
        {
            ClearPathMove();
            return;
        }

        var entitySystem = EntitySystem.Instance;
        int playerIndex = entitySystem.GetIndex(playerHandle);
        if (playerIndex < 0)
        {
            ClearPathMove();
            return;
        }

        Vector2Int direction = _pathMoveDirections.Peek();
        Vector2Int playerPosition = entitySystem.entities.coreComponents[playerIndex].Position;
        if (!CanExecutePathStep(entitySystem, playerPosition, direction, _pathMoveMode))
        {
            ClearPathMove();
            return;
        }

        _pathMoveDirections.Dequeue();
        if (!TrySubmitMove(direction))
            ClearPathMove();
    }

    private void TryConsumeBufferedMove()
    {
        if (_bufferedMoveDirection == Vector2Int.zero)
            return;

        if (Time.time > _bufferedMoveExpireTime)
        {
            ClearBufferedMove();
            return;
        }

        if (IsBeatMotionBusy())
            return;

        var direction = _bufferedMoveDirection;
        ClearBufferedMove();
        TrySubmitMove(direction);
    }

    private bool TryBufferMoveDuringBeatMotion(Vector2Int direction)
    {
        var drawSystem = DrawSystem.Instance;
        if (drawSystem == null || !drawSystem.IsBeatMotionBusy)
            return false;

        float busyUntil = drawSystem.BeatMotionBusyUntil;
        if (busyUntil - Time.time > movePreInputWindow)
            return true;

        _bufferedMoveDirection = direction;
        _bufferedMoveExpireTime = busyUntil + 0.05f;
        return true;
    }

    private bool IsBeatMotionBusy()
    {
        if (IntentSystem.Instance != null && IntentSystem.Instance.IsRunning)
            return true;

        var drawSystem = DrawSystem.Instance;
        return drawSystem != null && drawSystem.IsBeatMotionBusy;
    }

    private void ClearBufferedMove()
    {
        _bufferedMoveDirection = Vector2Int.zero;
        _bufferedMoveExpireTime = 0f;
    }

    private void ClearPathMove()
    {
        _pathMoveDirections.Clear();
        _pathMoveMode = PathMoveMode.None;
        _hasPathHoverCache = false;
        ClearPathPreview();
    }

    private void ClearMousePathMemory()
    {
        _mousePathMemoryCells.Clear();
        _hasMousePathMemoryStart = false;
        _mousePathMemoryStart = default;
    }

    private bool TrySubmitMove(Vector2Int direction)
    {
        if (!TryResolvePlayer(out var playerHandle))
            return false;

        var intent = IntentSystem.Instance.Request<MoveIntent>();
        intent.Setup(direction, MoveDistance);

        if (IntentSystem.Instance.SetPlayerIntent(playerHandle, IntentType.Move, intent))
        {
            TickSystem.PushTick();
            return true;
        }

        IntentSystem.Instance.Return(intent);
        return false;
    }

    private bool TrySubmitNoop()
    {
        if (!TryResolvePlayer(out var playerHandle))
            return false;

        var intent = IntentSystem.Instance.Request<NoopIntent>();
        intent.Setup();

        if (IntentSystem.Instance.SetPlayerIntent(playerHandle, IntentType.Noop, intent))
        {
            TickSystem.PushTick();
            return true;
        }

        IntentSystem.Instance.Return(intent);
        return false;
    }

    private void OnDisable()
    {
        ClearBufferedMove();
        ClearPathMove();
        ClearMousePathMemory();
        _isLeftPathDragging = false;
    }

    private static bool TryReadMoveDirection(out Vector2Int direction)
    {
        if (Input.GetKeyDown(KeyCode.W))
        {
            direction = Vector2Int.up;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.S))
        {
            direction = Vector2Int.down;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.A))
        {
            direction = Vector2Int.left;
            return true;
        }

        if (Input.GetKeyDown(KeyCode.D))
        {
            direction = Vector2Int.right;
            return true;
        }

        direction = Vector2Int.zero;
        return false;
    }

    private bool TryStartPathMove(PathMoveMode mode)
    {
        if (HandZone.IsAnyCardInteractionActive || IsPointerBlockedForStageInput())
            return false;

        ClearBufferedMove();
        ClearPathMove();

        if (!TryResolvePlayer(out var playerHandle))
            return false;

        var entitySystem = EntitySystem.Instance;
        int playerIndex = entitySystem.GetIndex(playerHandle);
        if (playerIndex < 0)
            return false;

        if (!TryGetMouseGridPosition(out var targetPosition))
            return false;

        Vector2Int startPosition = entitySystem.entities.coreComponents[playerIndex].Position;
        if (targetPosition == startPosition)
            return TrySubmitNoop();

        TrackMousePathMemory(startPosition, targetPosition);
        if (!TryBuildPreferredPath(entitySystem, startPosition, targetPosition, mode, _pathMoveDirections))
            return false;

        _pathMoveMode = mode;
        SetActivePathPreview(startPosition);
        TryConsumePathMove();
        return true;
    }

    private bool TryBeginLeftPathDrag()
    {
        if (HandZone.IsAnyCardInteractionActive || IsPointerBlockedForStageInput())
            return false;

        ClearBufferedMove();
        ClearPathMove();
        ClearMousePathMemory();
        _isLeftPathDragging = false;

        if (!TryResolvePlayer(out var playerHandle))
            return false;

        var entitySystem = EntitySystem.Instance;
        int playerIndex = entitySystem.GetIndex(playerHandle);
        if (playerIndex < 0)
            return false;

        Vector2Int startPosition = entitySystem.entities.coreComponents[playerIndex].Position;
        if (!TryGetMouseGridPosition(out var pressedCell) || pressedCell != startPosition)
            return false;

        _isLeftPathDragging = true;
        TrackMousePathMemory(startPosition, startPosition);
        return true;
    }

    private bool TryCommitLeftPathDrag()
    {
        if (!_isLeftPathDragging)
            return false;

        _isLeftPathDragging = false;

        if (!TryResolvePlayer(out var playerHandle))
        {
            ClearMousePathMemory();
            ClearPathPreview();
            return false;
        }

        var entitySystem = EntitySystem.Instance;
        int playerIndex = entitySystem.GetIndex(playerHandle);
        if (playerIndex < 0)
        {
            ClearMousePathMemory();
            ClearPathPreview();
            return false;
        }

        if (!TryGetMouseGridPosition(out var targetPosition)
            || !entitySystem.IsInsideMap(targetPosition)
            || IsCardAssistTarget(targetPosition))
        {
            ClearMousePathMemory();
            ClearPathPreview();
            return false;
        }

        ClearBufferedMove();
        ClearPathMove();

        Vector2Int startPosition = entitySystem.entities.coreComponents[playerIndex].Position;
        TrackMousePathMemory(startPosition, targetPosition);
        if (!TryBuildPreferredPath(entitySystem, startPosition, targetPosition, PathMoveMode.TerrainOnly, _pathMoveDirections))
        {
            ClearMousePathMemory();
            ClearPathPreview();
            return false;
        }

        _pathMoveMode = PathMoveMode.TerrainOnly;
        SetActivePathPreview(startPosition);
        TryConsumePathMove();
        return true;
    }

    private void CancelLeftPathDrag()
    {
        _isLeftPathDragging = false;
        ClearMousePathMemory();
        ClearPathPreview();
    }

    private bool TryHandleCardAssistClick()
    {
        if (!TryGetMouseGridPosition(out var targetCell) || !IsCardAssistTarget(targetCell))
            return false;

        if (HandZone.HasAssistSelection && !HandZone.IsAssistTargetCell(targetCell))
        {
            HandZone.ClearAssistSelectionActive();
            return true;
        }

        return HandZone.TryHandleAssistTargetClick(targetCell);
    }

    private static bool IsCardAssistTarget(Vector2Int cell)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsInsideMap(cell))
            return false;

        if (entitySystem.IsWall(cell))
            return true;

        EntityHandle occupant = entitySystem.GetOccupant(cell);
        if (!entitySystem.IsValid(occupant))
            return false;

        int index = entitySystem.GetIndex(occupant);
        if (index < 0)
            return false;

        EntityType type = entitySystem.entities.coreComponents[index].EntityType;
        return type == EntityType.Enemy || type == EntityType.Wall;
    }

    private static bool IsPointerBlockedForStageInput()
    {
        if (HandZone.IsAnyCardInteractionActive)
            return true;

        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private static bool IsPointerBlockedForStageInputExceptAssist()
    {
        if (HandZone.IsAnyCardAiming)
            return true;

        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private void UpdatePathPreview()
    {
        if (GridOverlayDrawSystem.Instance == null)
            return;

        if (HandZone.IsAnyCardInteractionActive || IsPointerBlockedForStageInputExceptAssist())
        {
            if (_isLeftPathDragging)
                _isLeftPathDragging = false;

            ClearMousePathMemory();
            ClearPathPreview();
            return;
        }

        if (!TryResolvePlayer(out var playerHandle))
        {
            ClearMousePathMemory();
            ClearPathPreview();
            return;
        }

        var entitySystem = EntitySystem.Instance;
        int playerIndex = entitySystem.GetIndex(playerHandle);
        if (playerIndex < 0)
        {
            ClearMousePathMemory();
            ClearPathPreview();
            return;
        }

        Vector2Int startPosition = entitySystem.entities.coreComponents[playerIndex].Position;
        if (_pathMoveDirections.Count > 0)
        {
            SetActivePathPreview(startPosition);
            return;
        }

        if (!TryGetMouseGridPosition(out var targetPosition)
            || IsCardAssistTarget(targetPosition)
            || !entitySystem.IsInsideMap(targetPosition))
        {
            ClearMousePathMemory();
            _hasPathHoverCache = false;
            ClearPathPreview();
            return;
        }

        bool memoryChanged = TrackMousePathMemory(startPosition, targetPosition);
        if (_hasPathHoverCache
            && _cachedPathHoverStart == startPosition
            && _cachedPathHoverTarget == targetPosition
            && !memoryChanged)
        {
            return;
        }

        _hasPathHoverCache = true;
        _cachedPathHoverStart = startPosition;
        _cachedPathHoverTarget = targetPosition;

        PathMoveMode previewMode = _isLeftPathDragging
            ? PathMoveMode.TerrainOnly
            : PathMoveMode.TerrainAndEntities;
        Queue<Vector2Int> previewDirections = _isLeftPathDragging
            ? _leftPathHoverDirections
            : _rightPathHoverDirections;
        bool hasPath = TryBuildPreferredPath(entitySystem, startPosition, targetPosition, previewMode, previewDirections);
        if (!hasPath)
        {
            ClearPathPreview();
            return;
        }

        ClearPathPreviewOverlay(ActivePathPreviewOverlayId);
        if (_isLeftPathDragging)
        {
            ClearPathPreviewOverlay(RightPathHoverOverlayId);
            SetPathPreviewFromDirections(
                LeftPathHoverOverlayId,
                startPosition,
                _leftPathHoverDirections,
                leftPathHoverColor,
                pathPreviewPriority);
        }
        else
        {
            ClearPathPreviewOverlay(LeftPathHoverOverlayId);
            SetPathPreviewFromDirections(
                RightPathHoverOverlayId,
                startPosition,
                _rightPathHoverDirections,
                rightPathHoverColor,
                pathPreviewPriority);
        }
    }

    private void SetActivePathPreview(Vector2Int startPosition)
    {
        ClearPathPreviewOverlay(LeftPathHoverOverlayId);
        ClearPathPreviewOverlay(RightPathHoverOverlayId);
        SetPathPreviewFromDirections(
            ActivePathPreviewOverlayId,
            startPosition,
            _pathMoveDirections,
            GetActivePathColor(),
            pathPreviewPriority);
    }

    private Color GetActivePathColor()
    {
        return _pathMoveMode == PathMoveMode.TerrainAndEntities
            ? rightActivePathColor
            : leftActivePathColor;
    }

    private void SetPathPreviewFromDirections(
        string overlayId,
        Vector2Int startPosition,
        Queue<Vector2Int> directions,
        Color color,
        int priority)
    {
        _pathPreviewCells.Clear();

        Vector2Int current = startPosition;
        Vector2Int[] directionArray = new Vector2Int[directions.Count];
        directions.CopyTo(directionArray, 0);
        for (int i = 0; i < directionArray.Length; i++)
        {
            Vector2Int direction = directionArray[i];
            current += direction;
            Vector2Int nextDirection = i + 1 < directionArray.Length ? directionArray[i + 1] : Vector2Int.zero;
            _pathPreviewCells.Add(new GridPathFlowOverlayCell(
                current,
                direction,
                nextDirection,
                i,
                directionArray.Length));
        }

        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null || _pathPreviewCells.Count == 0)
        {
            ClearPathPreviewOverlay(overlayId);
            return;
        }

        overlay.SetPathFlowOverlay(
            overlayId,
            _pathPreviewCells,
            color,
            pathPreviewHeight,
            priority);
    }

    private void ClearPathPreview()
    {
        _hasPathHoverCache = false;
        _pathPreviewCells.Clear();
        _leftPathHoverDirections.Clear();
        _rightPathHoverDirections.Clear();

        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.RemoveOverlay(ActivePathPreviewOverlayId);
        overlay.RemoveOverlay(LeftPathHoverOverlayId);
        overlay.RemoveOverlay(RightPathHoverOverlayId);
    }

    private static void ClearPathPreviewOverlay(string overlayId)
    {
        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.RemoveOverlay(overlayId);
    }

    private bool TrackMousePathMemory(Vector2Int startPosition, Vector2Int targetPosition)
    {
        if (!_hasMousePathMemoryStart
            || _mousePathMemoryStart != startPosition)
        {
            ClearMousePathMemory();
            _hasMousePathMemoryStart = true;
            _mousePathMemoryStart = startPosition;
        }

        if (_mousePathMemoryCells.Count > 0
            && !IsMooreNeighborOrSame(_mousePathMemoryCells[^1], targetPosition))
        {
            ClearMousePathMemory();
            _hasMousePathMemoryStart = true;
            _mousePathMemoryStart = startPosition;
        }

        if (_mousePathMemoryCells.Count > 0
            && _mousePathMemoryCells[^1] == targetPosition)
        {
            return false;
        }

        if (TryTrimMousePathMemoryToSegmentPoint(targetPosition, out bool trimmedToExistingTail))
            return !trimmedToExistingTail;

        int existingIndex = _mousePathMemoryCells.IndexOf(targetPosition);
        if (existingIndex >= 0)
        {
            int removeCount = _mousePathMemoryCells.Count - existingIndex - 1;
            if (removeCount > 0)
                _mousePathMemoryCells.RemoveRange(existingIndex + 1, removeCount);

            return removeCount > 0;
        }

        AddMousePathCell(targetPosition);
        while (_mousePathMemoryCells.Count > pathMemoryMaxCells)
            _mousePathMemoryCells.RemoveAt(0);

        return true;
    }

    private bool TryTrimMousePathMemoryToSegmentPoint(Vector2Int cell, out bool trimmedToExistingTail)
    {
        trimmedToExistingTail = false;

        for (int i = _mousePathMemoryCells.Count - 2; i >= 0; i--)
        {
            Vector2Int from = _mousePathMemoryCells[i];
            Vector2Int to = _mousePathMemoryCells[i + 1];
            if (!IsCellOnCardinalSegment(cell, from, to))
                continue;

            int removeStart = i + 2;
            if (removeStart < _mousePathMemoryCells.Count)
                _mousePathMemoryCells.RemoveRange(removeStart, _mousePathMemoryCells.Count - removeStart);

            if (cell == from)
            {
                _mousePathMemoryCells.RemoveAt(i + 1);
                trimmedToExistingTail = false;
                return true;
            }

            trimmedToExistingTail = _mousePathMemoryCells[i + 1] == cell;
            _mousePathMemoryCells[i + 1] = cell;
            return true;
        }

        return false;
    }

    private void AddMousePathCell(Vector2Int cell)
    {
        if (_mousePathMemoryCells.Count == 0)
        {
            _mousePathMemoryCells.Add(cell);
            return;
        }

        Vector2Int last = _mousePathMemoryCells[^1];
        if (last == cell)
            return;

        if (TryAppendMooreNeighborCellsBetween(last, cell))
            return;

        _mousePathMemoryCells.Add(cell);
    }

    private bool TryAppendMooreNeighborCellsBetween(Vector2Int from, Vector2Int to)
    {
        Vector2Int delta = to - from;
        if (Mathf.Abs(delta.x) > 1 || Mathf.Abs(delta.y) > 1)
            return false;

        if (delta == Vector2Int.zero)
            return true;

        if (delta.x != 0 && delta.y != 0)
        {
            Vector2Int horizontal = from + new Vector2Int(delta.x, 0);
            Vector2Int vertical = from + new Vector2Int(0, delta.y);
            _mousePathMemoryCells.Add(ChooseDiagonalBridgeCell(horizontal, vertical));
        }

        _mousePathMemoryCells.Add(to);
        while (_mousePathMemoryCells.Count > pathMemoryMaxCells)
            _mousePathMemoryCells.RemoveAt(0);

        return true;
    }

    private bool TryBuildPreferredPath(
        EntitySystem entitySystem,
        Vector2Int start,
        Vector2Int target,
        PathMoveMode mode,
        Queue<Vector2Int> result)
    {
        if (TryBuildRememberedPath(entitySystem, start, target, mode, result))
            return true;

        if (!TryBuildPath(entitySystem, start, target, mode, result))
            return false;

        SyncMousePathMemoryFromDirections(start, result);
        return true;
    }

    private bool TryBuildRememberedPath(
        EntitySystem entitySystem,
        Vector2Int start,
        Vector2Int target,
        PathMoveMode mode,
        Queue<Vector2Int> result)
    {
        result.Clear();

        if (!_hasMousePathMemoryStart
            || _mousePathMemoryStart != start
            || _mousePathMemoryCells.Count == 0
            || _mousePathMemoryCells[^1] != target)
        {
            return false;
        }

        if (_mousePathMemoryCells.Count < 2)
            return false;

        int firstKeptIndex = FindFirstConnectableMemoryIndex(entitySystem, start, mode, _pathMemoryConnectDirections);
        if (firstKeptIndex < 0)
            return false;

        if (firstKeptIndex > 0)
            _mousePathMemoryCells.RemoveRange(0, firstKeptIndex);

        CopyDirections(_pathMemoryConnectDirections, result);
        return AppendMemorySuffixDirections(entitySystem, mode, result);
    }

    private int FindFirstConnectableMemoryIndex(
        EntitySystem entitySystem,
        Vector2Int start,
        PathMoveMode mode,
        Queue<Vector2Int> connectDirections)
    {
        connectDirections.Clear();

        int firstCandidateIndex = Mathf.Max(0, _mousePathMemoryCells.Count - pathMemoryMaxWaypoints);
        for (int i = firstCandidateIndex; i < _mousePathMemoryCells.Count - 1; i++)
        {
            Vector2Int memoryCell = _mousePathMemoryCells[i];
            if (!TryBuildPath(entitySystem, start, memoryCell, mode, connectDirections))
                continue;

            if (IsConnectPathReasonable(connectDirections.Count, start, memoryCell))
                return i;
        }

        connectDirections.Clear();
        return -1;
    }

    private bool AppendMemorySuffixDirections(
        EntitySystem entitySystem,
        PathMoveMode mode,
        Queue<Vector2Int> result)
    {
        for (int i = 0; i < _mousePathMemoryCells.Count - 1; i++)
        {
            Vector2Int direction = _mousePathMemoryCells[i + 1] - _mousePathMemoryCells[i];
            if (!CanExecutePathStep(entitySystem, _mousePathMemoryCells[i], direction, mode))
            {
                result.Clear();
                return false;
            }

            result.Enqueue(direction);
        }

        return result.Count > 0;
    }

    private bool IsConnectPathReasonable(int connectLength, Vector2Int start, Vector2Int memoryCell)
    {
        if (connectLength <= 0)
            return false;

        int intendedDistance = ManhattanDistance(start, memoryCell);
        int allowedLength = Mathf.Max(
            intendedDistance + pathMemoryConnectMaxExtraSteps,
            Mathf.CeilToInt(intendedDistance * pathMemoryConnectMaxDetourRatio));
        return connectLength <= allowedLength;
    }

    private static void AppendDirections(Queue<Vector2Int> destination, Queue<Vector2Int> source)
    {
        foreach (var direction in source)
            destination.Enqueue(direction);
    }

    private static void CopyDirections(Queue<Vector2Int> source, Queue<Vector2Int> destination)
    {
        destination.Clear();
        AppendDirections(destination, source);
    }

    private void SyncMousePathMemoryFromDirections(Vector2Int start, Queue<Vector2Int> directions)
    {
        _mousePathMemoryCells.Clear();
        _hasMousePathMemoryStart = true;
        _mousePathMemoryStart = start;
        _mousePathMemoryCells.Add(start);

        Vector2Int current = start;
        foreach (var direction in directions)
        {
            current += direction;
            _mousePathMemoryCells.Add(current);
        }

        while (_mousePathMemoryCells.Count > pathMemoryMaxCells)
            _mousePathMemoryCells.RemoveAt(0);
    }

    private static int ManhattanDistance(Vector2Int first, Vector2Int second)
    {
        return Mathf.Abs(first.x - second.x) + Mathf.Abs(first.y - second.y);
    }

    private static bool IsMooreNeighborOrSame(Vector2Int first, Vector2Int second)
    {
        return Mathf.Abs(first.x - second.x) <= 1
               && Mathf.Abs(first.y - second.y) <= 1;
    }

    private Vector2Int ChooseDiagonalBridgeCell(Vector2Int horizontal, Vector2Int vertical)
    {
        int horizontalIndex = _mousePathMemoryCells.IndexOf(horizontal);
        int verticalIndex = _mousePathMemoryCells.IndexOf(vertical);
        if (horizontalIndex >= 0 && verticalIndex >= 0)
            return horizontalIndex >= verticalIndex ? horizontal : vertical;

        if (horizontalIndex >= 0)
            return horizontal;

        if (verticalIndex >= 0)
            return vertical;

        return horizontal;
    }

    private static bool IsCellOnCardinalSegment(Vector2Int cell, Vector2Int from, Vector2Int to)
    {
        if (from.x == to.x)
        {
            if (cell.x != from.x)
                return false;

            int minY = Mathf.Min(from.y, to.y);
            int maxY = Mathf.Max(from.y, to.y);
            return cell.y >= minY && cell.y <= maxY;
        }

        if (from.y == to.y)
        {
            if (cell.y != from.y)
                return false;

            int minX = Mathf.Min(from.x, to.x);
            int maxX = Mathf.Max(from.x, to.x);
            return cell.x >= minX && cell.x <= maxX;
        }

        return false;
    }

    private bool TryBuildPath(
        EntitySystem entitySystem,
        Vector2Int start,
        Vector2Int target,
        PathMoveMode mode,
        Queue<Vector2Int> result)
    {
        result.Clear();

        if (start == target)
            return false;

        if (!IsPathCellPassable(entitySystem, target, mode))
            return false;

        var entities = entitySystem.entities;
        int mapSize = entities.mapWidth * entities.mapHeight;
        if (mapSize <= 0)
            return false;

        int startIndex = ToMapIndex(entities, start);
        int targetIndex = ToMapIndex(entities, target);
        if (startIndex < 0 || startIndex >= mapSize || targetIndex < 0 || targetIndex >= mapSize)
            return false;

        int[] cameFrom = new int[mapSize];
        int[] gScore = new int[mapSize];
        bool[] closed = new bool[mapSize];
        bool[] inOpen = new bool[mapSize];
        for (int i = 0; i < mapSize; i++)
        {
            cameFrom[i] = -1;
            gScore[i] = int.MaxValue;
        }

        _pathOpenSet.Clear();
        _pathOpenSet.Add(startIndex);
        inOpen[startIndex] = true;
        gScore[startIndex] = 0;

        while (_pathOpenSet.Count > 0)
        {
            int currentIndex = PopBestOpenNode(_pathOpenSet, gScore, target, entities);
            inOpen[currentIndex] = false;

            if (currentIndex == targetIndex)
                return BuildDirectionQueue(cameFrom, startIndex, targetIndex, entities, result);

            closed[currentIndex] = true;
            Vector2Int current = FromMapIndex(entities, currentIndex);
            for (int i = 0; i < CrossDirections.Length; i++)
            {
                Vector2Int next = current + CrossDirections[i];
                if (!IsPathCellPassable(entitySystem, next, mode))
                    continue;

                int nextIndex = ToMapIndex(entities, next);
                if (nextIndex < 0 || nextIndex >= mapSize || closed[nextIndex])
                    continue;

                int tentative = gScore[currentIndex] + 1;
                if (tentative >= gScore[nextIndex])
                    continue;

                cameFrom[nextIndex] = currentIndex;
                gScore[nextIndex] = tentative;
                if (!inOpen[nextIndex])
                {
                    _pathOpenSet.Add(nextIndex);
                    inOpen[nextIndex] = true;
                }
            }
        }

        return false;
    }

    private static bool IsPathCellPassable(EntitySystem entitySystem, Vector2Int cell, PathMoveMode mode)
    {
        if (!entitySystem.IsInsideMap(cell) || entitySystem.IsWall(cell))
            return false;

        if (mode == PathMoveMode.TerrainAndEntities && entitySystem.GetOccupantId(cell) >= 0)
            return false;

        return true;
    }

    private static bool CanExecutePathStep(EntitySystem entitySystem, Vector2Int current, Vector2Int direction, PathMoveMode mode)
    {
        Vector2Int next = current + direction;
        if (!entitySystem.IsInsideMap(next) || entitySystem.IsWall(next))
            return false;

        int occupantId = entitySystem.GetOccupantId(next);
        if (occupantId < 0)
            return true;

        if (mode == PathMoveMode.TerrainAndEntities)
            return false;

        var occupant = entitySystem.GetHandleFromId(occupantId);
        int occupantIndex = entitySystem.GetIndex(occupant);
        if (occupantIndex < 0 || entitySystem.entities.coreComponents[occupantIndex].EntityType != EntityType.Box)
            return false;

        Vector2Int pushTarget = next + direction;
        return entitySystem.IsInsideMap(pushTarget)
               && !entitySystem.IsWall(pushTarget)
               && entitySystem.GetOccupantId(pushTarget) < 0;
    }

    private static int PopBestOpenNode(List<int> openSet, int[] gScore, Vector2Int target, EntityComponents entities)
    {
        int bestListIndex = 0;
        int bestNode = openSet[0];
        int bestScore = GetPathScore(bestNode, gScore, target, entities);
        for (int i = 1; i < openSet.Count; i++)
        {
            int node = openSet[i];
            int score = GetPathScore(node, gScore, target, entities);
            if (score >= bestScore)
                continue;

            bestScore = score;
            bestNode = node;
            bestListIndex = i;
        }

        openSet.RemoveAt(bestListIndex);
        return bestNode;
    }

    private static int GetPathScore(int mapIndex, int[] gScore, Vector2Int target, EntityComponents entities)
    {
        Vector2Int cell = FromMapIndex(entities, mapIndex);
        return gScore[mapIndex] + Mathf.Abs(cell.x - target.x) + Mathf.Abs(cell.y - target.y);
    }

    private static bool BuildDirectionQueue(int[] cameFrom, int startIndex, int targetIndex, EntityComponents entities, Queue<Vector2Int> result)
    {
        List<int> reversed = new();
        int current = targetIndex;
        while (current != startIndex)
        {
            if (current < 0)
                return false;

            reversed.Add(current);
            current = cameFrom[current];
        }

        if (reversed.Count == 0)
            return false;

        Vector2Int previous = FromMapIndex(entities, startIndex);
        for (int i = reversed.Count - 1; i >= 0; i--)
        {
            Vector2Int cell = FromMapIndex(entities, reversed[i]);
            result.Enqueue(cell - previous);
            previous = cell;
        }

        return result.Count > 0;
    }

    private static int ToMapIndex(EntityComponents entities, Vector2Int pos)
    {
        return pos.y * entities.mapWidth + pos.x;
    }

    private static Vector2Int FromMapIndex(EntityComponents entities, int index)
    {
        return new Vector2Int(index % entities.mapWidth, index / entities.mapWidth);
    }

    private bool TryGetMouseGridPosition(out Vector2Int gridPosition)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            gridPosition = default;
            return false;
        }

        var ray = camera.ScreenPointToRay(Input.mousePosition);
        var floorPlane = new Plane(Vector3.up, Vector3.zero);
        if (!floorPlane.Raycast(ray, out float distance))
        {
            gridPosition = default;
            return false;
        }

        Vector3 world = ray.GetPoint(distance);
        gridPosition = new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.z));
        return true;
    }

    private bool TryResolvePlayer(out EntityHandle playerHandle)
    {
        if (EntitySystem.Instance == null || !EntitySystem.Instance.IsInitialized)
        {
            playerHandle = EntityHandle.None;
            return false;
        }

        if (EntitySystem.Instance.IsValid(_playerHandle))
        {
            int idx = EntitySystem.Instance.GetIndex(_playerHandle);
            if (idx >= 0 && EntitySystem.Instance.entities.coreComponents[idx].EntityType == EntityType.Player)
            {
                playerHandle = _playerHandle;
                return true;
            }
            // 句柄有效但指向了非玩家实体 → 缓存失效，重新遍历
            _playerHandle = EntityHandle.None;
        }

        var entities = EntitySystem.Instance.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            _playerHandle = EntitySystem.Instance.GetHandleFromId(entities.coreComponents[i].Id);
            playerHandle = _playerHandle;
            return true;
        }

        playerHandle = EntityHandle.None;
        return false;
    }
}
