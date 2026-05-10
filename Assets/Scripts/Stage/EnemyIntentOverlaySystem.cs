using System.Collections.Generic;
using UnityEngine;

public class EnemyIntentOverlaySystem : MonoBehaviour
{
    private const string EnemyMoveOverlayId = "enemy_intent_move";
    private const string EnemyMoveDirectionOverlayId = "enemy_intent_move_direction";
    private const string EnemyAttackOverlayId = "enemy_intent_attack";

    [SerializeField] private Color moveColor = new(1f, 0.72f, 0.08f, 0.36f);
    [SerializeField] private Color moveDirectionColor = new(1f, 0.9f, 0.2f, 0.68f);
    [SerializeField] private Color attackColor = new(1f, 0.05f, 0.02f, 0.48f);
    [SerializeField] private float overlayHeight = 0.014f;

    private readonly List<Vector2Int> _moveCells = new();
    private readonly List<GridDirectionalOverlayCell> _moveDirections = new();
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

        _moveCells.Clear();
        _moveDirections.Clear();
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
                    AddMoveIntentCell(core.Position, intentComponent.Intent as MoveIntent);
                    break;
                case IntentType.Attack:
                    AddAttackIntentCells(intentComponent.Intent as AttackIntent);
                    break;
            }
        }

        overlay.SetOverlay(
            EnemyMoveOverlayId,
            _moveCells,
            GridOverlayStyle.SolidTint,
            moveColor,
            overlayHeight,
            20);

        overlay.SetDirectionalOverlay(
            EnemyMoveDirectionOverlayId,
            _moveDirections,
            moveDirectionColor,
            overlayHeight + 0.002f,
            21);

        overlay.SetOverlay(
            EnemyAttackOverlayId,
            _attackCells,
            GridOverlayStyle.Danger,
            attackColor,
            overlayHeight,
            22);
    }

    private void AddMoveIntentCell(Vector2Int origin, MoveIntent intent)
    {
        if (intent == null || !intent.Active || intent.Direction == Vector2Int.zero)
            return;

        int distance = Mathf.Max(1, intent.Distance);
        Vector2Int target = origin + intent.Direction * distance;
        AddUnique(_moveCells, target);
        AddUniqueDirection(_moveDirections, target, intent.Direction);
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

    private static void AddUniqueDirection(
        List<GridDirectionalOverlayCell> cells,
        Vector2Int cell,
        Vector2Int direction)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells[i].Cell == cell && cells[i].Direction == direction)
                return;
        }

        cells.Add(new GridDirectionalOverlayCell(cell, direction));
    }

    private static void Clear()
    {
        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.RemoveOverlay(EnemyMoveOverlayId);
        overlay.RemoveOverlay(EnemyMoveDirectionOverlayId);
        overlay.RemoveOverlay(EnemyAttackOverlayId);
    }
}
