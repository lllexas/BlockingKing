using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NekoGraph;
using UnityEngine;

public sealed class RuntimeLevelSolver : MonoBehaviour
{
    private static readonly Vector2Int[] MoveDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private readonly Queue<SolverNode> _frontier = new();
    private readonly List<SolverNode> _nodes = new();
    private readonly HashSet<ulong> _visited = new();
    private readonly List<SolverAction> _actions = new();
    private readonly List<CardSO> _handCards = new();
    private readonly List<Vector2Int> _cardCandidateCells = new();
    private readonly List<StatusEffectState> _statusEffects = new();

    private LevelSolverSession _session;
    private LevelPlayer _player;
    private Coroutine _routine;
    private bool _foundSolution;
    private int _solutionNodeIndex = -1;
    private int _expandedNodes;
    private int _executedTicks;
    private int _prunedNoChange;
    private int _prunedVisited;

    public void StartFromSession(LevelSolverSession session)
    {
        if (_routine != null)
            StopCoroutine(_routine);

        _session = session;
        _routine = StartCoroutine(RunSolverRoutine());
    }

    private IEnumerator RunSolverRoutine()
    {
        if (!TryInitialize())
            yield break;

        while (_frontier.Count > 0 && _nodes.Count < _session.maxNodes)
        {
            SolverNode node = _frontier.Dequeue();
            if (node.Depth >= _session.maxDepth)
                continue;

            LevelUndoSystem.Instance.RestoreRuntimeSnapshot(node.Snapshot);
            if (IsVictory())
            {
                RecordSolution(node.Index);
                if (_session.stopOnFirstSolution)
                    break;

                continue;
            }

            _expandedNodes++;
            EnumerateActions(_actions);

            for (int i = 0; i < _actions.Count; i++)
            {
                if (_nodes.Count >= _session.maxNodes)
                    break;

                LevelUndoSystem.Instance.RestoreRuntimeSnapshot(node.Snapshot);
                if (!ExecuteAction(_actions[i]))
                    continue;

                _executedTicks++;
                yield return null;
                while (IntentSystem.Instance != null && IntentSystem.Instance.IsRunning)
                    yield return null;

                if (LevelPlayer.ActiveInstance != null && LevelPlayer.ActiveInstance.LastResult == LevelPlayResult.Failure)
                    continue;

                ulong hash = ComputeCurrentStateHash();
                if (hash == node.Hash)
                {
                    _prunedNoChange++;
                    continue;
                }

                if (!_visited.Add(hash))
                {
                    _prunedVisited++;
                    continue;
                }

                var snapshot = LevelUndoSystem.Instance.CaptureRuntimeSnapshot();
                if (snapshot == null)
                    continue;

                var child = new SolverNode(
                    _nodes.Count,
                    node.Index,
                    node.Depth + 1,
                    snapshot,
                    _actions[i],
                    hash);
                _nodes.Add(child);

                _frontier.Enqueue(child);

                if (IsVictory())
                {
                    RecordSolution(child.Index);
                    if (_session.stopOnFirstSolution)
                        break;
                }
            }

            if (_foundSolution && _session.stopOnFirstSolution)
                break;
        }

        WriteReport();
        CleanupAndExitPlayMode();
    }

    private bool TryInitialize()
    {
        if (_session == null || !_session.active || _session.targetLevel == null)
        {
            Debug.LogError("[RuntimeLevelSolver] Missing active LevelSolverSession or target level.");
            CleanupAndExitPlayMode();
            return false;
        }

        _player = LevelPlayer.ActiveInstance != null ? LevelPlayer.ActiveInstance : FindObjectOfType<LevelPlayer>();
        if (_player == null)
        {
            Debug.LogError("[RuntimeLevelSolver] LevelPlayer not found.");
            CleanupAndExitPlayMode();
            return false;
        }

        var request = new LevelPlayRequest
        {
            Level = _session.targetLevel,
            Config = _session.config,
            Mode = LevelPlayMode.Classic
        };

        if (!_player.PlayLevel(request))
        {
            Debug.LogError("[RuntimeLevelSolver] Failed to play target level.");
            CleanupAndExitPlayMode();
            return false;
        }

        IntentSystem.Instance?.ConfigureSolverExecution(true);
        LevelUndoSystem.Instance?.SetCaptureSuppressed(true);
        ApplyInitialPlayerStats();
        IntentSystem.Instance?.ResolveWorldState();

        var initialSnapshot = LevelUndoSystem.Instance != null
            ? LevelUndoSystem.Instance.CaptureRuntimeSnapshot()
            : null;
        if (initialSnapshot == null)
        {
            Debug.LogError("[RuntimeLevelSolver] Failed to capture initial snapshot.");
            CleanupAndExitPlayMode();
            return false;
        }

        _frontier.Clear();
        _nodes.Clear();
        _visited.Clear();
        _foundSolution = false;
        _solutionNodeIndex = -1;
        _expandedNodes = 0;
        _executedTicks = 0;
        _prunedNoChange = 0;
        _prunedVisited = 0;

        ulong hash = ComputeCurrentStateHash();
        var root = new SolverNode(0, -1, 0, initialSnapshot, SolverAction.Root, hash);
        _nodes.Add(root);
        _frontier.Enqueue(root);
        _visited.Add(hash);
        Debug.Log($"[RuntimeLevelSolver] Started: level={_session.targetLevel.name}, maxDepth={_session.maxDepth}, maxNodes={_session.maxNodes}");
        return true;
    }

    private void RecordSolution(int nodeIndex)
    {
        if (_foundSolution)
            return;

        _foundSolution = true;
        _solutionNodeIndex = nodeIndex;
    }

    private void ApplyInitialPlayerStats()
    {
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return;

        int maxHp = Mathf.Max(1, _session.startingMaxHp);
        int hp = Mathf.Clamp(_session.startingHp, 1, maxHp);
        int attack = Mathf.Max(0, _session.startingAttack);
        int block = Mathf.Max(0, _session.startingBlock);
        var entities = entitySystem.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            EntityType type = entities.coreComponents[i].EntityType;
            bool isPlayer = type == EntityType.Player;
            bool isCoreBox = type == EntityType.Box && entities.propertyComponents[i].IsCore;
            if (!isPlayer && !isCoreBox)
                continue;

            ref var status = ref entities.statusComponents[i];
            status.BaseMaxHealth = maxHp;
            status.BaseAttack = isPlayer ? attack : 0;
            status.DamageTaken = Mathf.Max(0, maxHp - hp);
            status.Block = block;
            status.AttackModifier = 0;
            status.MaxHealthModifier = 0;
            entities.propertyComponents[i].Attack = CombatStats.GetAttack(status);
        }

        var statusFacade = GraphHub.Instance?.GetFacade<RunPlayerStatusFacade>();
        if (statusFacade == null)
        {
            statusFacade = new RunPlayerStatusFacade();
            GraphHub.Instance?.RegisterFacade(statusFacade);
        }

        statusFacade?.SetHp(hp, maxHp);
    }

    private void EnumerateActions(List<SolverAction> results)
    {
        results.Clear();
        if (!TryFindPlayer(out var player))
            return;

        for (int i = 0; i < MoveDirections.Length; i++)
            results.Add(SolverAction.Move(MoveDirections[i]));

        if (_session.includeNoop)
            results.Add(SolverAction.Noop());

        var hand = HandZone.ActiveInstance;
        if (hand == null)
            return;

        hand.CopyHandCards(_handCards);
        for (int i = 0; i < _handCards.Count; i++)
        {
            CardSO card = _handCards[i];
            if (card == null)
                continue;

            CardReleaseRuleRegistry.CollectCandidates(card, player, _cardCandidateCells);
            for (int c = 0; c < _cardCandidateCells.Count; c++)
            {
                if (!CardReleaseRuleRegistry.TryResolve(card, player, _cardCandidateCells[c], out var target))
                    continue;

                results.Add(SolverAction.PlayCard(i, card, target));
            }
        }
    }

    private bool ExecuteAction(in SolverAction action)
    {
        if (!TryFindPlayer(out var player))
            return false;

        var intentSystem = IntentSystem.Instance;
        if (intentSystem == null)
            return false;

        switch (action.Kind)
        {
            case SolverActionKind.Move:
            {
                var intent = intentSystem.Request<MoveIntent>();
                intent.Setup(action.Direction, 1);
                if (!intentSystem.SetPlayerIntent(player, IntentType.Move, intent))
                {
                    intentSystem.Return(intent);
                    return false;
                }

                TickSystem.PushTick();
                return true;
            }

            case SolverActionKind.Noop:
            {
                var intent = intentSystem.Request<NoopIntent>();
                intent.Setup();
                if (!intentSystem.SetPlayerIntent(player, IntentType.Noop, intent))
                {
                    intentSystem.Return(intent);
                    return false;
                }

                TickSystem.PushTick();
                return true;
            }

            case SolverActionKind.Card:
                return HandZone.ActiveInstance != null &&
                       HandZone.ActiveInstance.TryPlayCardAtIndex(action.HandIndex, action.Target, false);

            default:
                return false;
        }
    }

    private bool TryFindPlayer(out EntityHandle player)
    {
        player = EntityHandle.None;
        var entitySystem = EntitySystem.Instance;
        if (entitySystem == null || !entitySystem.IsInitialized || entitySystem.entities == null)
            return false;

        for (int i = 0; i < entitySystem.entities.entityCount; i++)
        {
            if (entitySystem.entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            player = entitySystem.GetHandleFromId(entitySystem.entities.coreComponents[i].Id);
            return entitySystem.IsValid(player);
        }

        return false;
    }

    private bool IsVictory()
    {
        return _session.victoryCondition == LevelSolverVictoryCondition.AllBoxesOnTargets &&
               LevelPlayer.ActiveInstance != null &&
               LevelPlayer.ActiveInstance.AreAllBoxesOnTargets();
    }

    private ulong ComputeCurrentStateHash()
    {
        ulong hash = 1469598103934665603UL;
        void Add(int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 1099511628211UL;
            }
        }

        void AddString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Add(0);
                return;
            }

            for (int i = 0; i < value.Length; i++)
                Add(value[i]);
        }

        var entitySystem = EntitySystem.Instance;
        if (entitySystem != null && entitySystem.IsInitialized && entitySystem.entities != null)
        {
            var entities = entitySystem.entities;
            Add(entities.entityCount);
            Add(entities.mapWidth);
            Add(entities.mapHeight);

            int groundLength = entities.groundMap != null ? entities.groundMap.Length : 0;
            Add(groundLength);
            for (int i = 0; i < groundLength; i++)
                Add(entities.groundMap[i]);

            for (int i = 0; i < entities.entityCount; i++)
            {
                ref var core = ref entities.coreComponents[i];
                ref var status = ref entities.statusComponents[i];
                ref var props = ref entities.propertyComponents[i];
                ref var counter = ref entities.counterComponents[i];
                ref var intent = ref entities.intentComponents[i];

                Add(core.Id);
                Add((int)core.EntityType);
                Add(core.Position.x);
                Add(core.Position.y);
                Add(core.OccupiesGrid ? 1 : 0);
                Add(status.BaseAttack);
                Add(status.BaseMaxHealth);
                Add(status.AttackModifier);
                Add(status.MaxHealthModifier);
                Add(status.DamageTaken);
                Add(status.Block);
                Add(status.BlockIdleTurns);
                Add(props.Attack);
                Add(props.Parameter0);
                Add(props.Parameter1);
                Add(props.SourceTagId);
                Add(props.IsCore ? 1 : 0);
                Add(props.SpawnInterval);
                Add(counter.NextTick <= 0 ? 0 : counter.NextTick - entitySystem.GlobalTick);
                Add((int)intent.Type);
                AddIntent(intent.Intent);
            }
        }

        if (HandZone.ActiveInstance != null)
        {
            var handSnapshot = HandZone.ActiveInstance.CaptureSnapshot();
            Add(handSnapshot.Initialized ? 1 : 0);
            Add(handSnapshot.ObservedHandStateRevision);
            AddCards(handSnapshot.Hand);
            AddCards(handSnapshot.DrawPile);
            AddCards(handSnapshot.DiscardPile);
        }

        _statusEffects.Clear();
        if (StatusEffectSystem.Instance != null)
            _statusEffects.AddRange(StatusEffectSystem.Instance.CaptureSnapshot());
        Add(_statusEffects.Count);
        for (int i = 0; i < _statusEffects.Count; i++)
        {
            var effect = _statusEffects[i];
            Add(effect.EntityId);
            Add(effect.EntityVersion);
            AddString(effect.EffectId);
            Add(effect.Stacks);
            Add((int)effect.DurationKind);
            Add(effect.SourceEntityId);
        }

        var player = LevelPlayer.ActiveInstance;
        if (player != null)
        {
            Add((int)player.LastResult);
            Add(player.RemainingSteps);
            Add(player.IsPlaying ? 1 : 0);
            Add(player.IsStageInputLocked ? 1 : 0);
        }

        return hash;

        void AddIntent(Intent value)
        {
            switch (value)
            {
                case MoveIntent move:
                    Add(move.Direction.x);
                    Add(move.Direction.y);
                    Add(move.Distance);
                    break;
                case AttackIntent attack:
                    Add(attack.TargetCount);
                    for (int i = 0; i < attack.TargetCount; i++)
                    {
                        Add(attack.TargetPositions[i].x);
                        Add(attack.TargetPositions[i].y);
                        Add(Mathf.RoundToInt(attack.DamageMultipliers[i] * 1000f));
                    }
                    break;
                case CardIntent card:
                    AddString(card.Card != null ? card.Card.cardId : null);
                    Add(card.PlayerCell.x);
                    Add(card.PlayerCell.y);
                    Add(card.TargetCell.x);
                    Add(card.TargetCell.y);
                    Add(card.Direction.x);
                    Add(card.Direction.y);
                    break;
                case SpawnIntent spawn:
                    Add(spawn.Origin.x);
                    Add(spawn.Origin.y);
                    AddString(spawn.EntityBP != null ? spawn.EntityBP.name : null);
                    break;
                case NoopIntent:
                    Add(17);
                    break;
            }
        }

        void AddCards(List<CardSO> cards)
        {
            int count = cards != null ? cards.Count : 0;
            Add(count);
            for (int i = 0; i < count; i++)
            {
                CardSO card = cards[i];
                AddString(card != null ? card.cardId : null);
                AddString(card != null ? card.instanceId : null);
            }
        }
    }

    private void WriteReport()
    {
        string reportPath = string.IsNullOrWhiteSpace(_session.reportPath)
            ? "Plan/Active/RuntimeLevelSolverReport.md"
            : _session.reportPath;
        string fullPath = Path.IsPathRooted(reportPath)
            ? reportPath
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", reportPath));

        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        var builder = new StringBuilder();
        builder.AppendLine("# Runtime Level Solver Report");
        builder.AppendLine();
        builder.AppendLine($"Level: `{(_session.targetLevel != null ? _session.targetLevel.name : "<null>")}`");
        builder.AppendLine($"Result: `{(_foundSolution ? "Solved" : "Unsolved")}`");
        builder.AppendLine($"Expanded nodes: `{_expandedNodes}`");
        builder.AppendLine($"Stored nodes: `{_nodes.Count}`");
        builder.AppendLine($"Visited states: `{_visited.Count}`");
        builder.AppendLine($"Executed ticks: `{_executedTicks}`");
        builder.AppendLine($"Pruned no-change actions: `{_prunedNoChange}`");
        builder.AppendLine($"Pruned visited states: `{_prunedVisited}`");
        builder.AppendLine($"Max depth: `{_session.maxDepth}`");
        builder.AppendLine($"Max nodes: `{_session.maxNodes}`");
        builder.AppendLine();

        if (_foundSolution && _solutionNodeIndex >= 0)
        {
            var path = BuildSolutionPath(_solutionNodeIndex);
            builder.AppendLine($"Solution depth: `{path.Count}`");
            builder.AppendLine();
            builder.AppendLine("## Actions");
            for (int i = 0; i < path.Count; i++)
                builder.AppendLine($"{i + 1}. {path[i].Describe()}");
        }
        else
        {
            builder.AppendLine("No solution found within configured limits.");
        }

        File.WriteAllText(fullPath, builder.ToString(), Encoding.UTF8);
        Debug.Log($"[RuntimeLevelSolver] Report written: {fullPath}");
    }

    private List<SolverAction> BuildSolutionPath(int nodeIndex)
    {
        var reversed = new List<SolverAction>();
        int current = nodeIndex;
        while (current > 0 && current < _nodes.Count)
        {
            var node = _nodes[current];
            reversed.Add(node.Action);
            current = node.ParentIndex;
        }

        reversed.Reverse();
        return reversed;
    }

    private void CleanupAndExitPlayMode()
    {
        IntentSystem.Instance?.ConfigureSolverExecution(false);
        LevelUndoSystem.Instance?.SetCaptureSuppressed(false);
        if (_session != null)
            _session.active = false;

#if UNITY_EDITOR
        if (_session != null)
        {
            UnityEditor.EditorUtility.SetDirty(_session);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        UnityEditor.EditorApplication.ExitPlaymode();
#endif
    }

    private readonly struct SolverNode
    {
        public readonly int Index;
        public readonly int ParentIndex;
        public readonly int Depth;
        public readonly LevelUndoSystem.LevelUndoSnapshot Snapshot;
        public readonly SolverAction Action;
        public readonly ulong Hash;

        public SolverNode(int index, int parentIndex, int depth, LevelUndoSystem.LevelUndoSnapshot snapshot, SolverAction action, ulong hash)
        {
            Index = index;
            ParentIndex = parentIndex;
            Depth = depth;
            Snapshot = snapshot;
            Action = action;
            Hash = hash;
        }
    }

    private enum SolverActionKind
    {
        Root,
        Move,
        Noop,
        Card
    }

    private readonly struct SolverAction
    {
        public static SolverAction Root => new(SolverActionKind.Root, Vector2Int.zero, -1, null, default);

        public readonly SolverActionKind Kind;
        public readonly Vector2Int Direction;
        public readonly int HandIndex;
        public readonly CardSO Card;
        public readonly CardReleaseTarget Target;

        private SolverAction(SolverActionKind kind, Vector2Int direction, int handIndex, CardSO card, CardReleaseTarget target)
        {
            Kind = kind;
            Direction = direction;
            HandIndex = handIndex;
            Card = card;
            Target = target;
        }

        public static SolverAction Move(Vector2Int direction)
        {
            return new SolverAction(SolverActionKind.Move, direction, -1, null, default);
        }

        public static SolverAction Noop()
        {
            return new SolverAction(SolverActionKind.Noop, Vector2Int.zero, -1, null, default);
        }

        public static SolverAction PlayCard(int handIndex, CardSO card, CardReleaseTarget target)
        {
            return new SolverAction(SolverActionKind.Card, Vector2Int.zero, handIndex, card, target);
        }

        public string Describe()
        {
            return Kind switch
            {
                SolverActionKind.Move => $"Move dir=({Direction.x},{Direction.y})",
                SolverActionKind.Noop => "Noop",
                SolverActionKind.Card => $"Card handIndex={HandIndex} card=`{(Card != null ? Card.cardId : "<null>")}` target=({Target.TargetCell.x},{Target.TargetCell.y}) dir=({Target.Direction.x},{Target.Direction.y})",
                _ => "Root"
            };
        }
    }
}
