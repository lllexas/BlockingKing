using System;
using System.Collections.Generic;
using NekoGraph;
using SpaceTUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(105)]
public sealed class RunShopPanelAnimator : SpaceUIAnimator
{
    protected override string UIID => RunRoundUIIds.ShopPanel;

    public static RunShopPanelAnimator Instance { get; private set; }

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private Button leaveButton;

    [Header("Selection")]
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Button buyButton;
    [SerializeField] private TextMeshProUGUI buyButtonText;

    [Header("Cards")]
    [SerializeField] private RectTransform[] cardAnchors = new RectTransform[5];
    [SerializeField] private TextMeshProUGUI[] cardPriceTexts = new TextMeshProUGUI[5];
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private Vector2 cardBaseSize = new(180f, 240f);
    [SerializeField] private Vector2 cardHoverSize = new(180f, 290f);
    [SerializeField] private bool disableCardInteraction = false;

    [Header("Items")]
    [SerializeField] private RectTransform[] itemAnchors = new RectTransform[5];
    [SerializeField] private TextMeshProUGUI[] itemPriceTexts = new TextMeshProUGUI[5];
    [SerializeField] private GameObject itemPrefab;

    private readonly HashSet<int> _soldOutIndices = new();
    private readonly List<SlotBinding> _cardBindings = new();
    private readonly List<SlotBinding> _itemBindings = new();
    private readonly List<CardView> _cardViews = new();
    private readonly List<ItemView> _itemViews = new();
    private ShopPayload _currentPayload;
    private ShopSO _directShop;
    private Action _directCloseCallback;
    private SlotBinding? _selectedBinding;
    private bool _subscribed;
    private bool _visible;

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        期望隐藏面板 += _ => HideShop();
        BindButtons();
        TrySubscribe();
    }

    private void OnEnable()
    {
        TrySubscribe();
        if (_currentPayload == null && ShopResource.TryConsumePendingPayload(out var payload))
            ShowShop(payload);
    }

    protected override void Update()
    {
        base.Update();
        TrySubscribe();
        if (_visible)
            RefreshGoldText();

        if (_currentPayload == null && ShopResource.TryConsumePendingPayload(out var payload))
            ShowShop(payload);
    }

    protected override void OnDestroy()
    {
        Unsubscribe();
        UnbindButtons();
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    public void OpenDirect(ShopSO shop, Action closeCallback = null)
    {
        if (shop == null)
            return;

        _currentPayload = null;
        _directShop = shop;
        _directCloseCallback = closeCallback;
        _soldOutIndices.Clear();
        _selectedBinding = null;
        ShowCurrentShop();
    }

    protected override void CloseAction()
    {
        LeaveShop();
    }

    private void TrySubscribe()
    {
        if (_subscribed || PostSystem.Instance == null)
            return;

        PostSystem.Instance.On(ShopResource.ExecuteEventName, OnShopExecute);
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || PostSystem.Instance == null)
            return;

        PostSystem.Instance.Off(ShopResource.ExecuteEventName, OnShopExecute);
        _subscribed = false;
    }

    private void OnShopExecute(object payload)
    {
        ShowShop(payload as ShopPayload);
    }

    private void ShowShop(ShopPayload payload)
    {
        if (payload?.Shop == null)
            return;

        _currentPayload = payload;
        _directShop = null;
        _directCloseCallback = null;
        _soldOutIndices.Clear();
        _selectedBinding = null;
        ShowCurrentShop();
    }

    private void ShowCurrentShop()
    {
        _visible = true;
        Refresh();
        this.FadeInIfHiddenPreserveRotation();
    }

    private void HideShop()
    {
        if (!_visible)
            return;

        _visible = false;
        this.FadeOutIfVisible();
    }

    private void Refresh()
    {
        var shop = ResolveShop();
        if (shop == null)
            return;

        SetText(titleText, string.IsNullOrWhiteSpace(shop.title) ? "商店" : shop.title);
        RefreshGoldText();
        BuildBindings(shop);
        RefreshCardAnchors();
        RefreshItemAnchors();
        if (_selectedBinding.HasValue && !ContainsBinding(_selectedBinding.Value))
            _selectedBinding = null;
        RefreshSelection();
    }

    private void BuildBindings(ShopSO shop)
    {
        _cardBindings.Clear();
        _itemBindings.Clear();

        var items = shop.GetItems();
        int count = items?.Count ?? 0;
        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            if (item == null)
                continue;

            var binding = new SlotBinding(i, item);
            if (item.kind == ShopSO.ShopItemKind.Card)
                _cardBindings.Add(binding);
            else
                _itemBindings.Add(binding);
        }
    }

    private void RefreshCardAnchors()
    {
        EnsureCardViewCount(Mathf.Min(_cardBindings.Count, cardAnchors?.Length ?? 0));

        for (int i = 0; i < _cardViews.Count; i++)
        {
            bool active = cardAnchors != null && i < cardAnchors.Length && i < _cardBindings.Count;
            var view = _cardViews[i];
            if (view == null)
                continue;

            view.gameObject.SetActive(active);
            if (!active)
                continue;

            var binding = _cardBindings[i];
            ParentToAnchor(view.RectTransform, cardAnchors[i]);
            view.Bind(
                binding.Item.card,
                _ => Select(binding),
                disableCardInteraction ? null : OnCardHoverEnter,
                disableCardInteraction ? null : OnCardHoverExit);
            view.SetLayoutSizes(cardBaseSize, cardHoverSize);
            view.SetInteractable(true);
            ResetRectTransform(view.RectTransform);
            RefreshPriceText(cardPriceTexts, i, binding);
        }

        HideUnusedPrices(cardPriceTexts, _cardBindings.Count);
    }

    private void RefreshItemAnchors()
    {
        EnsureItemViewCount(Mathf.Min(_itemBindings.Count, itemAnchors?.Length ?? 0));

        for (int i = 0; i < _itemViews.Count; i++)
        {
            bool active = itemAnchors != null && i < itemAnchors.Length && i < _itemBindings.Count;
            var view = _itemViews[i];
            if (view == null)
                continue;

            view.gameObject.SetActive(active);
            if (!active)
                continue;

            var binding = _itemBindings[i];
            ParentToAnchor(view.RectTransform, itemAnchors[i]);
            view.Bind(binding.Item.item, binding.Item.DisplayName, _ => Select(binding));
            ResetRectTransform(view.RectTransform);
            RefreshPriceText(itemPriceTexts, i, binding);
        }

        HideUnusedPrices(itemPriceTexts, _itemBindings.Count);
    }

    private void EnsureCardViewCount(int count)
    {
        if (cardPrefab == null)
        {
            if (count > 0)
                Debug.LogWarning("[RunShopPanelAnimator] Card Prefab is missing.");
            return;
        }

        while (_cardViews.Count < count)
        {
            var go = Instantiate(cardPrefab, transform);
            go.name = $"ShopCardView_{_cardViews.Count:000}";
            if (!go.TryGetComponent(out CardView view))
            {
                Debug.LogError("[RunShopPanelAnimator] Card Prefab must contain a CardView component.");
                Destroy(go);
                return;
            }

            _cardViews.Add(view);
        }
    }

    private void EnsureItemViewCount(int count)
    {
        if (itemPrefab == null)
        {
            if (count > 0)
                Debug.LogWarning("[RunShopPanelAnimator] Item Prefab is missing.");
            return;
        }

        while (_itemViews.Count < count)
        {
            var go = Instantiate(itemPrefab, transform);
            go.name = $"ShopItemView_{_itemViews.Count:000}";
            if (!go.TryGetComponent(out ItemView view))
            {
                Debug.LogError("[RunShopPanelAnimator] Item Prefab must contain an ItemView component.");
                Destroy(go);
                return;
            }

            _itemViews.Add(view);
        }
    }

    private void Select(SlotBinding binding)
    {
        _selectedBinding = binding;
        RefreshSelection();
    }

    private bool ContainsBinding(SlotBinding binding)
    {
        return _cardBindings.Exists(candidate => candidate.Index == binding.Index) ||
               _itemBindings.Exists(candidate => candidate.Index == binding.Index);
    }

    private void RefreshSelection()
    {
        if (!_selectedBinding.HasValue)
        {
            SetText(descriptionText, "选择商品查看详情");
            SetBuyButton(false, "购买");
            return;
        }

        var binding = _selectedBinding.Value;
        SetText(descriptionText, BuildItemBody(binding.Item));

        bool soldOut = _soldOutIndices.Contains(binding.Index);
        bool canBuy = !soldOut && CanBuy(binding.Item);
        SetBuyButton(canBuy, soldOut ? "已售出" : $"{Mathf.Max(0, binding.Item.price)} 金币");
    }

    private void SetBuyButton(bool interactable, string label)
    {
        if (buyButton != null)
            buyButton.interactable = interactable;

        SetText(buyButtonText, label);
    }

    private void TryBuySelected()
    {
        if (!_selectedBinding.HasValue)
            return;

        TryBuy(_selectedBinding.Value);
    }

    private void TryBuy(SlotBinding binding)
    {
        if (_soldOutIndices.Contains(binding.Index) || !CanBuy(binding.Item))
            return;

        var inventory = EnsureInventory();
        int price = Mathf.Max(0, binding.Item.price);
        if (inventory == null || (price > 0 && !inventory.TrySpendGold(price)))
            return;

        if (!GrantItem(inventory, binding.Item))
        {
            if (price > 0)
                inventory.AddGold(price);
            return;
        }

        _soldOutIndices.Add(binding.Index);
        Refresh();
    }

    private void LeaveShop()
    {
        if (_directShop != null)
        {
            _directShop = null;
            var callback = _directCloseCallback;
            _directCloseCallback = null;
            HideShop();
            callback?.Invoke();
            return;
        }

        if (_currentPayload == null)
        {
            HideShop();
            return;
        }

        var payload = _currentPayload;
        var runner = GraphHub.Instance?.DefaultRunner;
        if (runner == null)
        {
            _currentPayload = null;
            HideShop();
            return;
        }

        var targets = payload.Targets;
        if (targets == null || targets.Count == 0)
        {
            _currentPayload = null;
            HideShop();
            return;
        }

        bool resumed = runner.ResumeSuspendedSignalToTarget(
            payload.PackID,
            payload.SignalId,
            payload.SourceNodeId,
            targets[0].TargetNodeId);

        if (!resumed)
            return;

        _currentPayload = null;
        HideShop();
    }

    private ShopSO ResolveShop()
    {
        return _directShop != null ? _directShop : _currentPayload?.Shop;
    }

    private static bool CanBuy(ShopSO.ShopItem item)
    {
        return item != null && item.price >= 0 && GetGold() >= item.price && CanGrantItem(item);
    }

    private static bool CanGrantItem(ShopSO.ShopItem item)
    {
        if (item == null)
            return false;

        if (item.kind == ShopSO.ShopItemKind.Card)
            return item.card != null;

        return item.item != null || !string.IsNullOrWhiteSpace(item.inventoryItemId);
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

        string itemId = item.item != null ? item.item.ResolvedItemId : item.inventoryItemId;
        string itemType = item.item != null ? item.item.itemType : item.inventoryItemType;
        return inventory != null && inventory.AddItem(itemId, itemType, Mathf.Max(1, item.count));
    }

    private static RunInventoryFacade EnsureInventory()
    {
        var inventory = GraphHub.Instance?.GetFacade<RunInventoryFacade>();
        if (inventory == null)
        {
            inventory = new RunInventoryFacade();
            GraphHub.Instance?.RegisterFacade(inventory);
        }

        return inventory;
    }

    private static int GetGold()
    {
        return GraphHub.Instance?.GetFacade<RunInventoryFacade>()?.Gold ?? 0;
    }

    private void RefreshGoldText()
    {
        SetText(goldText, $"金币 {GetGold()}");
    }

    private void BindButtons()
    {
        if (leaveButton != null)
            leaveButton.onClick.AddListener(LeaveShop);

        if (buyButton != null)
            buyButton.onClick.AddListener(TryBuySelected);
    }

    private void UnbindButtons()
    {
        if (leaveButton != null)
            leaveButton.onClick.RemoveListener(LeaveShop);

        if (buyButton != null)
            buyButton.onClick.RemoveListener(TryBuySelected);
    }

    private static string BuildItemBody(ShopSO.ShopItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.description))
            return item.description;

        if (item.kind == ShopSO.ShopItemKind.Card && item.card != null)
            return string.IsNullOrWhiteSpace(item.card.description) ? "卡牌" : item.card.description;

        if (item.kind == ShopSO.ShopItemKind.InventoryItem && item.item != null)
            return string.IsNullOrWhiteSpace(item.item.description) ? "道具" : item.item.description;

        return string.IsNullOrWhiteSpace(item.inventoryItemType) ? "道具" : item.inventoryItemType;
    }

    private static void OnCardHoverEnter(CardView view)
    {
        view?.SetHoverState(true);
    }

    private static void OnCardHoverExit(CardView view)
    {
        view?.SetHoverState(false);
    }

    private static void RefreshPriceText(TextMeshProUGUI[] texts, int index, SlotBinding binding)
    {
        if (texts == null || index < 0 || index >= texts.Length || texts[index] == null)
            return;

        bool soldOut = false;
        if (RunShopPanelAnimator.Instance != null)
            soldOut = RunShopPanelAnimator.Instance._soldOutIndices.Contains(binding.Index);

        texts[index].gameObject.SetActive(true);
        texts[index].text = soldOut ? "已售出" : $"{Mathf.Max(0, binding.Item.price)} 金币";
    }

    private static void HideUnusedPrices(TextMeshProUGUI[] texts, int usedCount)
    {
        if (texts == null)
            return;

        for (int i = Mathf.Max(0, usedCount); i < texts.Length; i++)
        {
            if (texts[i] != null)
                texts[i].gameObject.SetActive(false);
        }
    }

    private static void ParentToAnchor(RectTransform child, RectTransform anchor)
    {
        if (child == null || anchor == null)
            return;

        child.SetParent(anchor, false);
    }

    private static void ResetRectTransform(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchoredPosition3D = Vector3.zero;
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }

    private readonly struct SlotBinding
    {
        public readonly int Index;
        public readonly ShopSO.ShopItem Item;

        public SlotBinding(int index, ShopSO.ShopItem item)
        {
            Index = index;
            Item = item;
        }
    }
}
