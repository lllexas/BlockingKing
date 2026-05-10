using System.Collections.Generic;
using UnityEngine;

public class EnemyIntentOverlaySystem : MonoBehaviour
{
    private const string EnemyMoveOverlayId = "enemy_intent_move";
    private const string EnemyAttackOverlayId = "enemy_intent_attack";

    [SerializeField] private Color moveColor = new(1f, 0.72f, 0.08f, 0.36f);
    [SerializeField] private Color attackColor = new(1f, 0.05f, 0.02f, 0.48f);
    [SerializeField] private float overlayHeight = 0.014f;

    private readonly List<Vector2Int> _moveCells = new();
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
        var intentSystem = IntentSystem.Instance;
        var overlay = GridOverlayDrawSystem.Instance;
        if (entitySystem == null || intentSystem == null || overlay == null || !entitySystem.IsInitialized)
        {
            Clear();
            return;
        }

        _moveCells.Clear();
        _attackCells.Clear();

        intentSystem.ForEachActiveIntent((actor, intentComponent) =>
        {
            int actorIndex = entitySystem.GetIndex(actor);
            if (actorIndex < 0)
                return;

            ref var core = ref entitySystem.entities.coreComponents[actorIndex];
            if (core.EntityType != EntityType.Enemy)
                return;

            switch (intentComponent.Type)
            {
                case IntentType.Move:
                    AddMoveIntentCell(core.Position, intentComponent.Intent as MoveIntent);
                    break;
                case IntentType.Attack:
                    AddAttackIntentCells(intentComponent.Intent as AttackIntent);
                    break;
            }
        });

        overlay.SetOverlay(
            EnemyMoveOverlayId,
            _moveCells,
            GridOverlayStyle.Path,
            moveColor,
            overlayHeight,
            20);

        overlay.SetOverlay(
            EnemyAttackOverlayId,
            _attackCells,
            GridOverlayStyle.Danger,
            attackColor,
            overlayHeight,
            21);
    }

    private void AddMoveIntentCell(Vector2Int origin, MoveIntent intent)
    {
        if (intent == null || !intent.Active || intent.Direction == Vector2Int.zero)
            return;

        int distance = Mathf.Max(1, intent.Distance);
        AddUnique(_moveCells, origin + intent.Direction * distance);
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

    private static void Clear()
    {
        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.RemoveOverlay(EnemyMoveOverlayId);
        overlay.RemoveOverlay(EnemyAttackOverlayId);
    }
}
