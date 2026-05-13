using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NekoGraph;
using UnityEngine;

public class CardDeckFacade : PackFacadeBase
{
    public const string EmptyPackID = "player_card_library";
    private const string CardsPath = "/cards.json";
    private const int WriteSubject = PackAccessSubjects.SystemMin;
    private const int ReadSubject = PackAccessSubjects.Player;

    protected override string GetDefaultPackID()
    {
        return EmptyPackID;
    }

    public BasePackData EnsureDeckPack()
    {
        var analyser = GraphHub.Instance?.DefaultAnalyser;
        if (analyser == null)
        {
            Debug.LogError("[CardDeckFacade] GraphAnalyser is not available.");
            return null;
        }

        var pack = analyser.EnsurePack(ResolvedPackID, WriteSubject);
        if (pack == null)
            return null;

        EnsureCardsFile(analyser);
        return pack;
    }

    public bool AddCard(CardSO card, int count = 1)
    {
        if (card == null)
            return false;

        if (count <= 0)
            return false;

        EnsureDeckPack();
        var database = LoadDatabase();
        for (int i = 0; i < count; i++)
        {
            var instance = CardResources.CreateInstanceFromTemplate(card);
            if (instance == null)
                return false;

            database.cards.Add(CreateEntry(instance));
        }

        bool saved = SaveDatabase(database);
        if (saved)
        {
            bool presentationPlayed = CardRewardPresentationHelper.TryPlayAddToDeck(card, count);
            Debug.Log($"[CardDeckFacade] Added card to deck: card={card.cardId}, count={count}, presentationPlayed={presentationPlayed}");
        }

        return saved;
    }

    public bool AddCard(string cardId, int count = 1)
    {
        if (string.IsNullOrWhiteSpace(cardId) || count <= 0)
            return false;

        EnsureDeckPack();
        var database = LoadDatabase();
        CardSO presentationCard = null;
        for (int i = 0; i < count; i++)
        {
            var instance = CardResources.CreateInstanceFromTemplate(cardId);
            if (instance == null)
                return false;

            presentationCard ??= instance;
            database.cards.Add(CreateEntry(instance));
        }

        bool saved = SaveDatabase(database);
        if (saved && presentationCard != null)
        {
            bool presentationPlayed = CardRewardPresentationHelper.TryPlayAddToDeck(presentationCard, count);
            Debug.Log($"[CardDeckFacade] Added card to deck by id: cardId={cardId}, count={count}, presentationPlayed={presentationPlayed}");
        }

        return saved;
    }

    public bool RemoveCard(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return false;

        var database = LoadDatabase();
        int removed = database.cards.RemoveAll(entry => entry.instanceId == instanceId);
        return removed > 0 && SaveDatabase(database);
    }

    public bool RemoveFirstCardById(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return false;

        var database = LoadDatabase();
        int index = database.cards.FindIndex(entry => entry.cardId == cardId);
        if (index < 0)
            return false;

        database.cards.RemoveAt(index);
        return SaveDatabase(database);
    }

    public bool ContainsCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return false;

        var database = LoadDatabase();
        return database.cards.Exists(entry => entry.cardId == cardId);
    }

    public int CountCard(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId))
            return 0;

        var database = LoadDatabase();
        return database.cards.FindAll(entry => entry.cardId == cardId).Count;
    }

    public List<CardDeckEntry> GetEntries()
    {
        return new List<CardDeckEntry>(LoadDatabase().cards);
    }

    public List<CardSO> GetCards()
    {
        var result = new List<CardSO>();
        foreach (var entry in LoadDatabase().cards)
        {
            var card = CardResources.FromJson(entry.inlineJson);
            if (card == null && CardResources.TryGetCard(entry.cardId, out var template))
                card = CardResources.CreateInstanceFromTemplate(template, entry.instanceId);

            if (card != null)
            {
                card.instanceId = entry.instanceId;
                result.Add(card);
            }
        }

        return result;
    }

    public bool ReplaceWithStartingDeck(StartingDeckSO startingDeck)
    {
        var database = new CardDeckDatabase();
        if (startingDeck?.cards != null)
        {
            foreach (var entry in startingDeck.cards)
            {
                if (entry == null || entry.card == null)
                    continue;

                int count = Math.Max(1, entry.count);
                for (int i = 0; i < count; i++)
                {
                    var instance = CardResources.CreateInstanceFromTemplate(entry.card);
                    if (instance == null)
                        return false;

                    database.cards.Add(CreateEntry(instance));
                }
            }
        }

        return SaveDatabase(database);
    }

    public bool Clear()
    {
        return SaveDatabase(new CardDeckDatabase());
    }

    private void EnsureCardsFile(GraphAnalyser analyser)
    {
        if (analyser.PathExists(ResolvedPackID, CardsPath, ReadSubject))
            return;

        analyser.WriteFile(ResolvedPackID, CardsPath, Serialize(new CardDeckDatabase()), WriteSubject);
    }

    private CardDeckDatabase LoadDatabase()
    {
        EnsureDeckPack();
        var node = GraphHub.Instance?.DefaultAnalyser?.GetNode(ResolvedPackID, CardsPath, ReadSubject) as VFSNodeData;
        if (node == null || string.IsNullOrWhiteSpace(node.InlineText))
            return new CardDeckDatabase();

        try
        {
            return JsonConvert.DeserializeObject<CardDeckDatabase>(node.InlineText) ?? new CardDeckDatabase();
        }
        catch (Exception e)
        {
            Debug.LogError($"[CardDeckFacade] Failed to parse card deck database: {e.Message}");
            return new CardDeckDatabase();
        }
    }

    private bool SaveDatabase(CardDeckDatabase database)
    {
        var analyser = GraphHub.Instance?.DefaultAnalyser;
        if (analyser == null)
            return false;

        EnsureDeckPack();
        return analyser.WriteFile(ResolvedPackID, CardsPath, Serialize(database ?? new CardDeckDatabase()), WriteSubject);
    }

    private static string Serialize(CardDeckDatabase database)
    {
        return JsonConvert.SerializeObject(database, Formatting.Indented);
    }

    private static CardDeckEntry CreateEntry(CardSO card)
    {
        if (string.IsNullOrWhiteSpace(card.instanceId))
            card.instanceId = Guid.NewGuid().ToString("N");

        return new CardDeckEntry
        {
            instanceId = card.instanceId,
            cardId = card.cardId,
            inlineJson = CardResources.ToJson(card)
        };
    }

    [Serializable]
    public sealed class CardDeckDatabase
    {
        public List<CardDeckEntry> cards = new List<CardDeckEntry>();
    }

    [Serializable]
    public sealed class CardDeckEntry
    {
        public string instanceId;
        public string cardId;
        public string inlineJson;
    }
}
