using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public static class CardResources
{
    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    private static readonly Dictionary<string, CardSO> _cardById = new Dictionary<string, CardSO>();
    private static bool _loaded;

    public static bool TryGetCard(string cardId, out CardSO card)
    {
        EnsureLoaded();
        return _cardById.TryGetValue(cardId, out card) && card != null;
    }

    public static CardSO GetCard(string cardId)
    {
        return TryGetCard(cardId, out var card) ? card : null;
    }

    public static IReadOnlyCollection<CardSO> GetAllCards()
    {
        EnsureLoaded();
        return _cardById.Values;
    }

    public static CardSO CreateInstanceFromTemplate(string cardId, string instanceId = null)
    {
        if (!TryGetCard(cardId, out var template))
            return null;

        return CreateInstanceFromTemplate(template, instanceId);
    }

    public static CardSO CreateInstanceFromTemplate(CardSO template, string instanceId = null)
    {
        if (template == null)
            return null;

        var card = FromJson(ToJson(template));
        card.name = template.name;
        card.icon = template.icon;
        card.instanceId = string.IsNullOrWhiteSpace(instanceId) ? System.Guid.NewGuid().ToString("N") : instanceId;
        if (string.IsNullOrWhiteSpace(card.cardId))
            card.cardId = !string.IsNullOrWhiteSpace(template.cardId) ? template.cardId : template.name;
        return card;
    }

    public static string ToJson(CardSO card)
    {
        return card == null ? string.Empty : JsonConvert.SerializeObject(card, JsonSettings);
    }

    public static CardSO FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var card = ScriptableObject.CreateInstance<CardSO>();
        JsonConvert.PopulateObject(json, card, JsonSettings);
        if (!string.IsNullOrWhiteSpace(card.cardId) && TryGetCard(card.cardId, out var template))
        {
            card.name = template.name;
            card.icon = template.icon;
        }
        else
        {
            card.name = string.IsNullOrWhiteSpace(card.displayName) ? "CardInstance" : card.displayName;
        }

        return card;
    }

    public static void Reload()
    {
        _loaded = false;
        _cardById.Clear();
        EnsureLoaded();
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        _cardById.Clear();

        foreach (var card in Resources.LoadAll<CardSO>(string.Empty))
        {
            Register(card);
        }

        foreach (var meta in MetaLib.GetAllMetas())
        {
            if (meta == null || string.IsNullOrWhiteSpace(meta.EffectiveID))
                continue;

            if (meta.Kind != MetaLib.EntryKind.ResourceObject)
                continue;

            var card = MetaLib.GetObject<CardSO>(meta.EffectiveID);
            Register(card);
        }
    }

    private static void Register(CardSO card)
    {
        if (card == null)
            return;

        string id = !string.IsNullOrWhiteSpace(card.cardId) ? card.cardId : card.name;
        if (string.IsNullOrWhiteSpace(id))
            return;

        _cardById[id] = card;
    }
}
