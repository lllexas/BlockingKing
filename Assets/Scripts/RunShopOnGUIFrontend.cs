using System.Collections.Generic;
using NekoGraph;
using UnityEngine;

[DefaultExecutionOrder(110)]
public class RunShopOnGUIFrontend : MonoBehaviour
{
    private readonly HashSet<int> _soldOutIndices = new HashSet<int>();
    private ShopPayload _currentShop;
    private Vector2 _scroll;
    private bool _subscribed;
    private GUIStyle _boxStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _bodyStyle;
    private GUIStyle _goldStyle;
    private GUIStyle _itemTitleStyle;
    private GUIStyle _itemBodyStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _disabledButtonStyle;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        if (PostSystem.Instance != null)
            PostSystem.Instance.Off(ShopResource.ExecuteEventName, OnShopExecute);

        _subscribed = false;
    }

    private void Update()
    {
        TrySubscribe();

        if (_currentShop == null && ShopResource.TryConsumePendingPayload(out var payload))
            ShowShop(payload);
    }

    private void TrySubscribe()
    {
        if (_subscribed || PostSystem.Instance == null)
            return;

        PostSystem.Instance.On(ShopResource.ExecuteEventName, OnShopExecute);
        _subscribed = true;
    }

    private void OnShopExecute(object payload)
    {
        ShowShop(payload as ShopPayload);
    }

    private void ShowShop(ShopPayload payload)
    {
        if (payload?.Shop == null)
            return;

        _currentShop = payload;
        _soldOutIndices.Clear();
        _scroll = Vector2.zero;
    }

    private void OnGUI()
    {
        var payload = _currentShop;
        var shop = payload?.Shop;
        if (shop == null)
            return;

        EnsureStyles();
        int oldDepth = GUI.depth;
        GUI.depth = -30;

        DrawSolidRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.58f));

        float width = Mathf.Clamp(Screen.width * 0.74f, 920f, 1400f);
        float height = Mathf.Clamp(Screen.height * 0.76f, 680f, 900f);
        var rect = new Rect(
            (Screen.width - width) * 0.5f,
            (Screen.height - height) * 0.5f,
            width,
            height);

        GUILayout.BeginArea(rect, _boxStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label(string.IsNullOrWhiteSpace(shop.title) ? "商店" : shop.title, _titleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label($"金币 {GetGold()}", _goldStyle, GUILayout.Width(180f));
        GUILayout.EndHorizontal();

        if (!string.IsNullOrWhiteSpace(shop.description))
        {
            GUILayout.Space(8f);
            GUILayout.Label(shop.description, _bodyStyle);
        }

        GUILayout.Space(18f);
        _scroll = GUILayout.BeginScrollView(_scroll);
        DrawItems(shop);
        GUILayout.EndScrollView();

        GUILayout.Space(16f);
        if (GUILayout.Button("离开", _buttonStyle, GUILayout.Height(58f)))
        {
            LeaveShop();
            GUILayout.EndArea();
            GUI.depth = oldDepth;
            return;
        }

        GUILayout.EndArea();
        GUI.depth = oldDepth;
    }

    private void DrawItems(ShopSO shop)
    {
        int count = shop.items?.Count ?? 0;
        if (count == 0)
        {
            GUILayout.Label("暂时没有商品。", _bodyStyle);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var item = shop.items[i];
            if (item == null)
                continue;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical();
            GUILayout.Label(BuildItemTitle(item), _itemTitleStyle);
            GUILayout.Label(BuildItemBody(item), _itemBodyStyle);
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            bool soldOut = _soldOutIndices.Contains(i);
            bool canBuy = !soldOut && CanBuy(item);
            GUI.enabled = canBuy;
            string buttonText = soldOut ? "已售出" : $"{item.price} 金币";
            if (GUILayout.Button(buttonText, canBuy ? _buttonStyle : _disabledButtonStyle, GUILayout.Width(180f), GUILayout.Height(56f)))
                TryBuy(i, item);
            GUI.enabled = true;

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(8f);
        }
    }

    private bool CanBuy(ShopSO.ShopItem item)
    {
        return item != null && item.price >= 0 && GetGold() >= item.price && CanGrantItem(item);
    }

    private bool CanGrantItem(ShopSO.ShopItem item)
    {
        if (item.kind == ShopSO.ShopItemKind.Card)
            return item.card != null;

        return !string.IsNullOrWhiteSpace(item.inventoryItemId);
    }

    private void TryBuy(int index, ShopSO.ShopItem item)
    {
        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        int price = Mathf.Max(0, item.price);
        if (inventory == null || (price > 0 && !inventory.TrySpendGold(price)))
            return;

        if (!GrantItem(inventory, item))
        {
            if (price > 0)
                inventory.AddGold(price);
            return;
        }

        _soldOutIndices.Add(index);
    }

    private static bool GrantItem(RunInventoryFacade inventory, ShopSO.ShopItem item)
    {
        if (item.kind == ShopSO.ShopItemKind.Card)
        {
            var deck = GraphHub.Instance?.GetFacade<CardDeckFacade>();
            if (deck == null)
            {
                deck = new CardDeckFacade();
                GraphHub.Instance?.RegisterFacade(deck);
            }

            return deck != null && deck.AddCard(item.card, Mathf.Max(1, item.count));
        }

        return inventory.AddItem(
            item.inventoryItemId,
            item.inventoryItemType,
            Mathf.Max(1, item.count));
    }

    private void LeaveShop()
    {
        if (_currentShop == null)
            return;

        var runner = GraphHub.Instance?.DefaultRunner;
        if (runner == null)
            return;

        var targets = _currentShop.Targets;
        if (targets == null || targets.Count == 0)
        {
            _currentShop = null;
            return;
        }

        bool resumed = runner.ResumeSuspendedSignalToTarget(
            _currentShop.PackID,
            _currentShop.SignalId,
            _currentShop.SourceNodeId,
            targets[0].TargetNodeId);

        if (resumed)
            _currentShop = null;
    }

    private static int GetGold()
    {
        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
            return 0;

        return inventory.Gold;
    }

    private static string BuildItemTitle(ShopSO.ShopItem item)
    {
        string name = item.DisplayName;
        if (string.IsNullOrWhiteSpace(name))
            name = "未命名商品";

        return item.count > 1 ? $"{name} x{item.count}" : name;
    }

    private static string BuildItemBody(ShopSO.ShopItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.description))
            return item.description;

        if (item.kind == ShopSO.ShopItemKind.Card && item.card != null)
            return string.IsNullOrWhiteSpace(item.card.description) ? "卡牌" : item.card.description;

        return string.IsNullOrWhiteSpace(item.inventoryItemType) ? "道具" : item.inventoryItemType;
    }

    private void EnsureStyles()
    {
        _boxStyle ??= new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(28, 28, 24, 24)
        };

        _titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };

        _bodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };

        _goldStyle ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        _itemTitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };

        _itemBodyStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 19,
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };

        _buttonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };

        _disabledButtonStyle ??= new GUIStyle(GUI.skin.button)
        {
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
    }

    private static void DrawSolidRect(Rect rect, Color color)
    {
        Color oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = oldColor;
    }
}
