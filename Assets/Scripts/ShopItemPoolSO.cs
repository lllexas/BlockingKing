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
        [TableColumnWidth(72)]
        public ShopSO.ShopItemKind kind = ShopSO.ShopItemKind.Card;

        [ShowIf(nameof(IsCard))]
        [TableColumnWidth(120)]
        [AssetsOnly]
        public CardSO card;

        [ShowIf(nameof(IsInventoryItem))]
        [TableColumnWidth(120)]
        [AssetsOnly]
        public ItemSO item;

        [TableColumnWidth(70)]
        [Min(1)]
        public int count = 1;

        [TableColumnWidth(80)]
        [Min(0)]
        public int price = 50;

        [TextArea(1, 3)]
        public string description;

        private bool IsCard => kind == ShopSO.ShopItemKind.Card;
        private bool IsInventoryItem => kind == ShopSO.ShopItemKind.InventoryItem;

        public ShopSO.ShopItem ToShopItem()
        {
            return new ShopSO.ShopItem
            {
                kind = kind,
                card = card,
                item = item,
                count = count,
                price = price,
                description = description
            };
        }
    }

    [TableList(AlwaysExpanded = true, DrawScrollView = true, MinScrollViewHeight = 160)]
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
