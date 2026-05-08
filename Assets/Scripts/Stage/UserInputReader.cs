using UnityEngine;

/// <summary>
/// 监听玩家输入，写入玩家单位 Intent，并推进 Tick。
/// </summary>
public class UserInputReader : MonoBehaviour
{
    private const int MoveDistance = 1;

    private EntityHandle _playerHandle = EntityHandle.None;

    private void Update()
    {
        if (!TryReadMoveDirection(out var direction))
            return;

        if (!TryResolvePlayer(out var playerHandle))
            return;

        var intent = IntentSystem.Instance.Request<MoveIntent>();
        intent.Setup(direction, MoveDistance);

        if (IntentSystem.Instance.SetIntent(playerHandle, IntentType.Move, intent))
            TickSystem.PushTick();
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

    private bool TryResolvePlayer(out EntityHandle playerHandle)
    {
        if (EntitySystem.Instance == null || !EntitySystem.Instance.IsInitialized)
        {
            playerHandle = EntityHandle.None;
            return false;
        }

        if (EntitySystem.Instance.IsValid(_playerHandle))
        {
            playerHandle = _playerHandle;
            return true;
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
