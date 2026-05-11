using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "ShopItemPool", menuName = "BlockingKing/Pool/Shop Item Pool")]
public class ShopItemPoolSO : ContentPoolSO<ShopItemPoolSO.Entry, ShopSO.ShopItem>
{
    [Serializable]
    public sealed class Entry : PoolEntryBase
    {
        [AssetsOnly]
        public CardSO card;

        public string itemId;
        public ShopSO.ShopItemKind kind = ShopSO.ShopItemKind.Card;
        public string inventoryItemId;
        public string inventoryItemType;

        [Min(1)]
        public int count = 1;

        [Min(0)]
        public int price = 50;

        [TextArea(1, 3)]
        public string description;

        public ShopSO.ShopItem ToShopItem()
        {
            return new ShopSO.ShopItem
            {
                itemId = itemId,
                kind = kind,
                card = card,
                inventoryItemId = inventoryItemId,
                inventoryItemType = inventoryItemType,
                count = count,
                price = price,
                description = description
            };
        }
    }

    public List<Entry> entries = new List<Entry>();

    public override IReadOnlyList<Entry> Entries => entries;

    protected override string GetEntryDisplayName(Entry entry, int index)
    {
        if (entry == null)
            return base.GetEntryDisplayName(entry, index);

        var item = entry.ToShopItem();
        return string.IsNullOrWhiteSpace(item.DisplayName) ? $"Shop Item {index}" : item.DisplayName;
    }
}
