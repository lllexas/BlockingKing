using System;
using System.Collections.Generic;
using UnityEngine;

public enum CardReleaseRule
{
    Self,
    OrthogonalLine,
    DiagonalLine,
    EightWayLine,
    DiagonalStep,
    KnightJump
}

public readonly struct CardReleaseTarget
{
    public readonly Vector2Int PlayerCell;
    public readonly Vector2Int TargetCell;
    public readonly Vector2Int Direction;
    public readonly bool HasDirection;

    public CardReleaseTarget(Vector2Int playerCell, Vector2Int targetCell, Vector2Int direction)
    {
        PlayerCell = playerCell;
        TargetCell = targetCell;
        Direction = direction;
        HasDirection = direction != Vector2Int.zero;
    }
}

public static class CardReleaseRuleRegistry
{
    public delegate bool RuleResolver(Vector2Int playerCell, Vector2Int selectedCell, out CardReleaseTarget target);

    private static readonly Vector2Int[] OrthogonalDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private static readonly Vector2Int[] DiagonalDirections =
    {
        new Vector2Int(1, 1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, -1)
    };

    private static readonly Vector2Int[] KnightOffsets =
    {
        new Vector2Int(1, 2),
        new Vector2Int(2, 1),
        new Vector2Int(2, -1),
        new Vector2Int(1, -2),
        new Vector2Int(-1, -2),
        new Vector2Int(-2, -1),
        new Vector2Int(-2, 1),
        new Vector2Int(-1, 2)
    };

    private static readonly Dictionary<CardReleaseRule, RuleResolver> Resolvers = new Dictionary<CardReleaseRule, RuleResolver>
    {
        { CardReleaseRule.Self, ResolveSelf },
        { CardReleaseRule.OrthogonalLine, ResolveOrthogonalLine },
        { CardReleaseRule.DiagonalLine, ResolveDiagonalLine },
        { CardReleaseRule.EightWayLine, ResolveEightWayLine },
        { CardReleaseRule.DiagonalStep, ResolveDiagonalStep },
        { CardReleaseRule.KnightJump, ResolveKnightJump }
    };

    public static bool TryResolve(CardSO card, EntityHandle player, Vector2Int selectedCell, out CardReleaseTarget target)
    {
        target = default;
        if (card == null)
            return false;

        if (!TryGetPlayerCell(player, out var playerCell))
            return false;

        return TryResolve(card.releaseRule, playerCell, selectedCell, out target);
    }

    public static bool TryResolveWithoutSelectedCell(CardSO card, EntityHandle player, out CardReleaseTarget target)
    {
        target = default;
        if (card == null || RequiresSelectedCell(card.releaseRule))
            return false;

        if (!TryGetPlayerCell(player, out var playerCell))
            return false;

        return TryResolve(card.releaseRule, playerCell, playerCell, out target);
    }

    public static void CollectCandidates(CardSO card, EntityHandle player, List<Vector2Int> results)
    {
        if (results == null)
            return;

        results.Clear();
        if (card == null || !TryGetPlayerCell(player, out var playerCell))
            return;

        CollectCandidates(card.releaseRule, playerCell, results);
    }

    public static void CollectCandidates(CardReleaseRule rule, Vector2Int playerCell, List<Vector2Int> results)
    {
        if (results == null)
            return;

        results.Clear();

        switch (rule)
        {
            case CardReleaseRule.Self:
                AddIfInside(results, playerCell);
                break;

            case CardReleaseRule.OrthogonalLine:
                CollectRays(playerCell, OrthogonalDirections, results);
                break;

            case CardReleaseRule.DiagonalLine:
                CollectRays(playerCell, DiagonalDirections, results);
                break;

            case CardReleaseRule.EightWayLine:
                CollectRays(playerCell, OrthogonalDirections, results);
                CollectRays(playerCell, DiagonalDirections, results);
                break;

            case CardReleaseRule.DiagonalStep:
                CollectOffsets(playerCell, DiagonalDirections, results);
                break;

            case CardReleaseRule.KnightJump:
                CollectOffsets(playerCell, KnightOffsets, results);
                break;
        }
    }

    public static bool RequiresSelectedCell(CardReleaseRule rule)
    {
        return rule != CardReleaseRule.Self;
    }

    public static void Register(CardReleaseRule rule, RuleResolver resolver)
    {
        if (resolver == null)
            return;

        Resolvers[rule] = resolver;
    }

    public static bool TryResolve(CardReleaseRule rule, Vector2Int playerCell, Vector2Int selectedCell, out CardReleaseTarget target)
    {
        target = default;
        return Resolvers.TryGetValue(rule, out var resolver) && resolver(playerCell, selectedCell, out target);
    }

    private static bool ResolveSelf(Vector2Int playerCell, Vector2Int selectedCell, out CardReleaseTarget target)
    {
        target = new CardReleaseTarget(playerCell, playerCell, Vector2Int.zero);
        return true;
    }

    private static bool ResolveOrthogonalLine(Vector2Int playerCell, Vector2Int selectedCell, out CardReleaseTarget target)
    {
        Vector2Int delta = selectedCell - playerCell;
        if (delta == Vector2Int.zero || (delta.x != 0 && delta.y != 0))
        {
            target = default;
            return false;
        }

        Vector2Int direction = new Vector2Int(Math.Sign(delta.x), Math.Sign(delta.y));
        target = new CardReleaseTarget(playerCell, selectedCell, direction);
        return true;
    }

    private static bool ResolveDiagonalLine(Vector2Int playerCell, Vector2Int selectedCell, out CardReleaseTarget target)
    {
        Vector2Int delta = selectedCell - playerCell;
        if (delta == Vector2Int.zero || Mathf.Abs(delta.x) != Mathf.Abs(delta.y))
        {
            target = default;
            return false;
        }

        Vector2Int direction = new Vector2Int(Math.Sign(delta.x), Math.Sign(delta.y));
        target = new CardReleaseTarget(playerCell, selectedCell, direction);
        return true;
    }

    private static bool ResolveEightWayLine(Vector2Int playerCell, Vector2Int selectedCell, out CardReleaseTarget target)
    {
        if (ResolveOrthogonalLine(playerCell, selectedCell, out target))
            return true;

        return ResolveDiagonalLine(playerCell, selectedCell, out target);
    }

    private static bool ResolveDiagonalStep(Vector2Int playerCell, Vector2Int selectedCell, out CardReleaseTarget target)
    {
        Vector2Int delta = selectedCell - playerCell;
        if (Mathf.Abs(delta.x) != 1 || Mathf.Abs(delta.y) != 1)
        {
            target = default;
            return false;
        }

        target = new CardReleaseTarget(playerCell, selectedCell, delta);
        return true;
    }

    private static bool ResolveKnightJump(Vector2Int playerCell, Vector2Int selectedCell, out CardReleaseTarget target)
    {
        Vector2Int delta = selectedCell - playerCell;
        int absX = Mathf.Abs(delta.x);
        int absY = Mathf.Abs(delta.y);
        if (!((absX == 1 && absY == 2) || (absX == 2 && absY == 1)))
        {
            target = default;
            return false;
        }

        target = new CardReleaseTarget(playerCell, selectedCell, delta);
        return true;
    }

    private static bool TryGetPlayerCell(EntityHandle player, out Vector2Int playerCell)
    {
        playerCell = default;
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || !entitySystem.IsValid(player))
            return false;

        int playerIndex = entitySystem.GetIndex(player);
        if (playerIndex < 0)
            return false;

        playerCell = entitySystem.entities.coreComponents[playerIndex].Position;
        return true;
    }

    private static void CollectRays(Vector2Int origin, IReadOnlyList<Vector2Int> directions, List<Vector2Int> results)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized)
            return;

        for (int i = 0; i < directions.Count; i++)
        {
            Vector2Int direction = directions[i];
            Vector2Int cell = origin + direction;
            while (entitySystem.IsInsideMap(cell))
            {
                AddIfInside(results, cell);
                cell += direction;
            }
        }
    }

    private static void CollectOffsets(Vector2Int origin, IReadOnlyList<Vector2Int> offsets, List<Vector2Int> results)
    {
        for (int i = 0; i < offsets.Count; i++)
            AddIfInside(results, origin + offsets[i]);
    }

    private static void AddIfInside(List<Vector2Int> results, Vector2Int cell)
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem != null && entitySystem.IsInitialized && !entitySystem.IsInsideMap(cell))
            return;

        if (!results.Contains(cell))
            results.Add(cell);
    }
}
