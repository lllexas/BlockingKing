using System;
using Newtonsoft.Json;
using NekoGraph;
using UnityEngine;

public class RunPlayerStatusFacade : PackFacadeBase
{
    public const string DefaultPackID = "player_run_status";
    private const string StatusPath = "/status.json";
    private const int WriteSubject = PackAccessSubjects.SystemMin;
    private const int ReadSubject = PackAccessSubjects.Player;

    protected override string GetDefaultPackID()
    {
        return DefaultPackID;
    }

    public int MaxHp => LoadDatabase().maxHp;
    public int CurrentHp => LoadDatabase().currentHp;
    public bool IsDead => CurrentHp <= 0;

    public BasePackData EnsureStatusPack()
    {
        var analyser = GraphHub.Instance?.DefaultAnalyser;
        if (analyser == null)
        {
            Debug.LogError("[RunPlayerStatusFacade] GraphAnalyser is not available.");
            return null;
        }

        var pack = analyser.EnsurePack(ResolvedPackID, WriteSubject);
        if (pack == null)
            return null;

        EnsureStatusFile(analyser);
        return pack;
    }

    public bool Reset(int maxHp, int currentHp)
    {
        maxHp = Mathf.Max(1, maxHp);
        currentHp = Mathf.Clamp(currentHp <= 0 ? maxHp : currentHp, 0, maxHp);
        return SaveDatabase(new RunPlayerStatusDatabase
        {
            maxHp = maxHp,
            currentHp = currentHp
        });
    }

    public bool SetHp(int currentHp, int maxHp = -1)
    {
        var database = LoadDatabase();
        if (maxHp > 0)
            database.maxHp = Mathf.Max(1, maxHp);

        database.currentHp = Mathf.Clamp(currentHp, 0, Mathf.Max(1, database.maxHp));
        return SaveDatabase(database);
    }

    public bool Heal(int amount)
    {
        if (amount <= 0)
            return false;

        var database = LoadDatabase();
        database.currentHp = Mathf.Clamp(database.currentHp + amount, 0, Mathf.Max(1, database.maxHp));
        return SaveDatabase(database);
    }

    public bool Damage(int amount)
    {
        if (amount <= 0)
            return false;

        var database = LoadDatabase();
        database.currentHp = Mathf.Clamp(database.currentHp - amount, 0, Mathf.Max(1, database.maxHp));
        return SaveDatabase(database);
    }

    private void EnsureStatusFile(GraphAnalyser analyser)
    {
        if (analyser.PathExists(ResolvedPackID, StatusPath, ReadSubject))
            return;

        analyser.WriteFile(ResolvedPackID, StatusPath, Serialize(new RunPlayerStatusDatabase()), WriteSubject);
    }

    private RunPlayerStatusDatabase LoadDatabase()
    {
        EnsureStatusPack();
        var node = GraphHub.Instance?.DefaultAnalyser?.GetNode(ResolvedPackID, StatusPath, ReadSubject) as VFSNodeData;
        if (node == null || string.IsNullOrWhiteSpace(node.InlineText))
            return new RunPlayerStatusDatabase();

        try
        {
            var database = JsonConvert.DeserializeObject<RunPlayerStatusDatabase>(node.InlineText) ?? new RunPlayerStatusDatabase();
            database.maxHp = Mathf.Max(1, database.maxHp);
            database.currentHp = Mathf.Clamp(database.currentHp, 0, database.maxHp);
            return database;
        }
        catch (Exception e)
        {
            Debug.LogError($"[RunPlayerStatusFacade] Failed to parse status database: {e.Message}");
            return new RunPlayerStatusDatabase();
        }
    }

    private bool SaveDatabase(RunPlayerStatusDatabase database)
    {
        var analyser = GraphHub.Instance?.DefaultAnalyser;
        if (analyser == null)
            return false;

        EnsureStatusPack();
        database ??= new RunPlayerStatusDatabase();
        database.maxHp = Mathf.Max(1, database.maxHp);
        database.currentHp = Mathf.Clamp(database.currentHp, 0, database.maxHp);
        return analyser.WriteFile(ResolvedPackID, StatusPath, Serialize(database), WriteSubject);
    }

    private static string Serialize(RunPlayerStatusDatabase database)
    {
        return JsonConvert.SerializeObject(database, Formatting.Indented);
    }

    [Serializable]
    public sealed class RunPlayerStatusDatabase
    {
        public int maxHp = 30;
        public int currentHp = 30;
    }
}
