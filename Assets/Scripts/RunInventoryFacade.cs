using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NekoGraph;
using UnityEngine;

public class RunInventoryFacade : PackFacadeBase
{
    public const string DefaultPackID = "player_run_inventory";
    private const string InventoryPath = "/inventory.json";
    private const int WriteSubject = PackAccessSubjects.SystemMin;
    private const int ReadSubject = PackAccessSubjects.Player;

    protected override string GetDefaultPackID()
    {
        return DefaultPackID;
    }

    public int Gold => LoadDatabase().gold;

    public BasePackData EnsureInventoryPack()
    {
        var analyser = GraphHub.Instance?.DefaultAnalyser;
        if (analyser == null)
        {
            Debug.LogError("[RunInventoryFacade] GraphAnalyser is not available.");
            return null;
        }

        var pack = analyser.EnsurePack(ResolvedPackID, WriteSubject);
        if (pack == null)
            return null;

        EnsureInventoryFile(analyser);
        return pack;
    }

    public bool Reset(int startingGold = 0)
    {
        return SaveDatabase(new RunInventoryDatabase
        {
            gold = Math.Max(0, startingGold)
        });
    }

    public bool SetGold(int value)
    {
        var database = LoadDatabase();
        database.gold = Math.Max(0, value);
        return SaveDatabase(database);
    }

    public bool AddGold(int amount)
    {
        if (amount <= 0)
            return false;

        var database = LoadDatabase();
        database.gold += amount;
        return SaveDatabase(database);
    }

    public bool TrySpendGold(int amount)
    {
        if (amount <= 0)
            return false;

        var database = LoadDatabase();
        if (database.gold < amount)
            return false;

        database.gold -= amount;
        return SaveDatabase(database);
    }

    public List<RunInventoryItemEntry> GetItems()
    {
        return new List<RunInventoryItemEntry>(LoadDatabase().items);
    }

    public bool AddItem(string itemId, string itemType, int count = 1, string inlineJson = null)
    {
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            return false;

        var database = LoadDatabase();
        var entry = database.items.Find(item =>
            string.Equals(item.itemId, itemId, StringComparison.Ordinal) &&
            string.Equals(item.itemType, itemType, StringComparison.Ordinal) &&
            string.Equals(item.inlineJson, inlineJson, StringComparison.Ordinal));

        if (entry == null)
        {
            database.items.Add(new RunInventoryItemEntry
            {
                itemId = itemId,
                itemType = itemType,
                count = count,
                inlineJson = inlineJson
            });
        }
        else
        {
            entry.count += count;
        }

        return SaveDatabase(database);
    }

    public bool RemoveItem(string itemId, int count = 1, string itemType = null)
    {
        if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            return false;

        var database = LoadDatabase();
        var entry = database.items.Find(item =>
            string.Equals(item.itemId, itemId, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(itemType) || string.Equals(item.itemType, itemType, StringComparison.Ordinal)));

        if (entry == null || entry.count < count)
            return false;

        entry.count -= count;
        if (entry.count <= 0)
            database.items.Remove(entry);

        return SaveDatabase(database);
    }

    public bool ContainsItem(string itemId, string itemType = null)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        var database = LoadDatabase();
        return database.items.Exists(item =>
            string.Equals(item.itemId, itemId, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(itemType) || string.Equals(item.itemType, itemType, StringComparison.Ordinal)));
    }

    private void EnsureInventoryFile(GraphAnalyser analyser)
    {
        if (analyser.PathExists(ResolvedPackID, InventoryPath, ReadSubject))
            return;

        analyser.WriteFile(ResolvedPackID, InventoryPath, Serialize(new RunInventoryDatabase()), WriteSubject);
    }

    private RunInventoryDatabase LoadDatabase()
    {
        EnsureInventoryPack();
        var node = GraphHub.Instance?.DefaultAnalyser?.GetNode(ResolvedPackID, InventoryPath, ReadSubject) as VFSNodeData;
        if (node == null || string.IsNullOrWhiteSpace(node.InlineText))
            return new RunInventoryDatabase();

        try
        {
            return JsonConvert.DeserializeObject<RunInventoryDatabase>(node.InlineText) ?? new RunInventoryDatabase();
        }
        catch (Exception e)
        {
            Debug.LogError($"[RunInventoryFacade] Failed to parse inventory database: {e.Message}");
            return new RunInventoryDatabase();
        }
    }

    private bool SaveDatabase(RunInventoryDatabase database)
    {
        var analyser = GraphHub.Instance?.DefaultAnalyser;
        if (analyser == null)
            return false;

        EnsureInventoryPack();
        return analyser.WriteFile(ResolvedPackID, InventoryPath, Serialize(database ?? new RunInventoryDatabase()), WriteSubject);
    }

    private static string Serialize(RunInventoryDatabase database)
    {
        return JsonConvert.SerializeObject(database, Formatting.Indented);
    }

    [Serializable]
    public sealed class RunInventoryDatabase
    {
        public int gold;
        public List<RunInventoryItemEntry> items = new List<RunInventoryItemEntry>();
    }

    [Serializable]
    public sealed class RunInventoryItemEntry
    {
        public string itemId;
        public string itemType;
        public int count;
        public string inlineJson;
    }
}
