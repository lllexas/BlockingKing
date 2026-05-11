using System;
using System.Collections.Generic;
using NekoGraph;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "Shop", menuName = "BlockingKing/Run Stage/Shop")]
[VFSContentKind(VFSContentKind.UnityObject)]
public class ShopSO : ScriptableObject
{
    public enum ShopItemKind
    {
        Card,
        InventoryItem
    }

    [Serializable]
    public sealed class ShopItem
    {
        public ShopItemKind kind = ShopItemKind.Card;

        [ShowIf(nameof(IsCard))]
        [AssetsOnly]
        public CardSO card;

        [ShowIf(nameof(IsInventoryItem))]
        [AssetsOnly]
        public ItemSO item;

        [ShowIf(nameof(ShowItemFallbackFields))]
        public string inventoryItemId;

        [ShowIf(nameof(ShowItemFallbackFields))]
        public string inventoryItemType;

        [Min(1)]
        public int count = 1;

        [Min(0)]
        public int price = 50;

        [TextArea(1, 3)]
        public string description;

        private bool IsCard => kind == ShopItemKind.Card;
        private bool IsInventoryItem => kind == ShopItemKind.InventoryItem;
        private bool ShowItemFallbackFields => IsInventoryItem && item == null;

        public string DisplayName
        {
            get
            {
                if (kind == ShopItemKind.Card && card != null)
                    return string.IsNullOrWhiteSpace(card.displayName) ? card.name : card.displayName;

                if (kind == ShopItemKind.InventoryItem && item != null)
                    return item.ResolvedDisplayName;

                return inventoryItemId;
            }
        }
    }

    [Header("Pool")]
    public ShopItemPoolSO itemPool;

    public string shopId;
    public string title = "商店";

    [TextArea(2, 5)]
    public string description;

    public List<ShopItem> items = new List<ShopItem>();

    public IReadOnlyList<ShopItem> GetItems()
    {
        if (itemPool != null && itemPool.Entries != null && itemPool.Entries.Count > 0)
        {
            var result = new List<ShopItem>();
            foreach (var entry in itemPool.Entries)
            {
                if (entry == null || !entry.enabled)
                    continue;

                result.Add(entry.ToShopItem());
            }

            if (result.Count > 0)
                return result;
        }

        return items;
    }
}
