using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 监听玩家输入，写入玩家单位 Intent，并推进 Tick。
/// </summary>
public class UserInputReader : MonoBehaviour
{
    private const int MoveDistance = 1;

    [Header("Move Input")]
    [SerializeField, Min(0f)] private float movePreInputWindow = 0.15f;

    private EntityHandle _playerHandle = EntityHandle.None;
    private Vector2Int _bufferedMoveDirection;
    private float _bufferedMoveExpireTime;
    private PathMoveMode _pathMoveMode;
    private readonly Queue<Vector2Int> _pathMoveDirections = new();
    private readonly List<int> _pathOpenSet = new();

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

        if (IsPointerBlockedForStageInput())
            return;

        if (Input.GetMouseButtonDown(0))
        {
            TryStartPathMove(PathMoveMode.TerrainOnly);
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            TryStartPathMove(PathMoveMode.TerrainAndEntities);
            return;
        }

        if (!hasMoveKey)
            return;

        ClearPathMove();

        if (TryBufferMoveDuringBeatMotion(direction))
            return;

        TrySubmitMove(direction);
    }

    private void TryConsumePathMove()
    {
        if (_pathMoveDirections.Count == 0)
            return;

        if (HandZone.IsAnyCardInteractionActive)
            return;

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

    private void OnDisable()
    {
        ClearBufferedMove();
        ClearPathMove();
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
        if (!TryBuildPath(entitySystem, startPosition, targetPosition, mode, _pathMoveDirections))
            return false;

        _pathMoveMode = mode;
        TryConsumePathMove();
        return true;
    }

    private static bool IsPointerBlockedForStageInput()
    {
        if (HandZone.IsAnyCardInteractionActive)
            return true;

        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
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
