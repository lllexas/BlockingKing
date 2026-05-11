using System.Collections.Generic;
using UnityEngine;

public class EnemyIntentOverlaySystem : MonoBehaviour
{
    private const string EnemyMovePathOverlayId = "enemy_intent_move_path";
    private const string EnemyAttackOverlayId = "enemy_intent_attack";

    [SerializeField] private Color moveColor = new(1f, 0.72f, 0.08f, 0.36f);
    [SerializeField] private Color attackColor = new(1f, 0.05f, 0.02f, 0.48f);
    [SerializeField] private float overlayHeight = 0.014f;
    [SerializeField] private int movePathPriority = 20;
    [SerializeField] private int attackPriority = 22;

    private readonly List<GridPathFlowOverlayCell> _movePathCells = new();
    private readonly List<Vector2Int> _attackCells = new();

    private void LateUpdate()
    {
        Refresh();
    }

    private void OnDisable()
    {
        Clear();
    }

    private void Refresh()
    {
        var entitySystem = EntitySystem.Instance;
        var overlay = GridOverlayDrawSystem.Instance;
        if (entitySystem == null || overlay == null || !entitySystem.IsInitialized)
        {
            Clear();
            return;
        }

        _movePathCells.Clear();
        _attackCells.Clear();

        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            ref var core = ref entities.coreComponents[i];
            if (core.EntityType != EntityType.Enemy)
                continue;

            var intentComponent = entities.intentComponents[i];
            if (intentComponent.Type == IntentType.None || intentComponent.Intent == null)
                continue;

            switch (intentComponent.Type)
            {
                case IntentType.Move:
                    AddMoveIntentPath(core.Position, intentComponent.Intent as MoveIntent);
                    break;
                case IntentType.Attack:
                    AddAttackIntentCells(intentComponent.Intent as AttackIntent);
                    break;
            }
        }

        overlay.SetPathFlowOverlay(
            EnemyMovePathOverlayId,
            _movePathCells,
            moveColor,
            overlayHeight,
            movePathPriority);

        overlay.SetOverlay(
            EnemyAttackOverlayId,
            _attackCells,
            GridOverlayStyle.Danger,
            attackColor,
            overlayHeight,
            attackPriority);
    }

    private void AddMoveIntentPath(Vector2Int origin, MoveIntent intent)
    {
        if (intent == null || !intent.Active || intent.Direction == Vector2Int.zero)
            return;

        int distance = Mathf.Max(1, intent.Distance);
        Vector2Int current = origin;
        for (int i = 0; i < distance; i++)
        {
            current += intent.Direction;
            Vector2Int nextDirection = i + 1 < distance ? intent.Direction : Vector2Int.zero;
            AddMovePathCell(new GridPathFlowOverlayCell(
                current,
                intent.Direction,
                nextDirection,
                _movePathCells.Count,
                distance));
        }
    }

    private void AddAttackIntentCells(AttackIntent intent)
    {
        if (intent == null || !intent.Active)
            return;

        for (int i = 0; i < intent.TargetCount; i++)
            AddUnique(_attackCells, intent.TargetPositions[i]);
    }

    private static void AddUnique(List<Vector2Int> cells, Vector2Int cell)
    {
        if (!cells.Contains(cell))
            cells.Add(cell);
    }

    private void AddMovePathCell(GridPathFlowOverlayCell cell)
    {
        for (int i = 0; i < _movePathCells.Count; i++)
        {
            var existing = _movePathCells[i];
            if (existing.Cell == cell.Cell
                && existing.IncomingDirection == cell.IncomingDirection
                && existing.OutgoingDirection == cell.OutgoingDirection)
            {
                return;
            }
        }

        _movePathCells.Add(cell);
    }

    private static void Clear()
    {
        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.RemoveOverlay(EnemyMovePathOverlayId);
        overlay.RemoveOverlay(EnemyAttackOverlayId);
    }
}
