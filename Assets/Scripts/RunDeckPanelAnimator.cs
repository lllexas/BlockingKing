using System.Collections.Generic;
using System.Collections;
using NekoGraph;
using SpaceTUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RunDeckPanelAnimator : SpaceUIAnimator
{
    protected override string UIID => RunRoundUIIds.DeckPanel;

    public static RunDeckPanelAnimator Instance { get; private set; }

    [Header("Deck UI")]
    [SerializeField] private RectTransform cardRoot;
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private TextMeshProUGUI emptyText;
    [SerializeField] private Button closeButton;
    [SerializeField] private Vector2 cardBaseSize = new(180f, 240f);
    [SerializeField] private Vector2 cardHoverSize = new(180f, 290f);
    [SerializeField] private bool disableCardInteraction = true;

    private readonly List<CardView> _views = new();
    private Coroutine _layoutRefreshRoutine;
    private bool _visible;

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        期望显示面板 += _ => ShowDeck();
        期望隐藏面板 += _ => HideDeck();
        BindButtons();
    }

    protected override void OnDestroy()
    {
        UnbindButtons();
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    public void Toggle()
    {
        if (_visible)
            HideDeck();
        else
            ShowDeck();
    }

    public void ShowDeck()
    {
        _visible = true;
        Refresh();
        this.FadeInIfHiddenPreserveRotation();
        ScheduleLayoutRefresh();
    }

    public void HideDeck()
    {
        if (!_visible)
            return;

        _visible = false;
        this.FadeOutIfVisible();
    }

    protected override void CloseAction()
    {
        HideDeck();
    }

    private void BindButtons()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(HideDeck);
    }

    private void UnbindButtons()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(HideDeck);
    }

    private void Refresh()
    {
        var cards = LoadDeckCards();
        EnsureViewCount(cards.Count);

        for (int i = 0; i < _views.Count; i++)
        {
            bool active = i < cards.Count;
            var view = _views[i];
            if (view == null)
                continue;

            view.gameObject.SetActive(active);
            if (!active)
                continue;

            view.Bind(
                cards[i],
                null,
                disableCardInteraction ? null : OnCardHoverEnter,
                disableCardInteraction ? null : OnCardHoverExit);
            view.SetLayoutSizes(cardBaseSize, cardHoverSize);
            view.SetInteractable(!disableCardInteraction);
            ResetLayoutDrivenTransform(view.RectTransform);
        }

        if (countText != null)
            countText.text = cards.Count.ToString();

        if (emptyText != null)
            emptyText.gameObject.SetActive(cards.Count == 0);

        if (scrollRect != null)
            scrollRect.normalizedPosition = new Vector2(0f, 1f);

        ForceRefreshLayout();
    }

    private void EnsureViewCount(int count)
    {
        if (cardRoot == null || cardPrefab == null)
        {
            if (count > 0)
                Debug.LogWarning("[RunDeckPanelAnimator] Card Root or Card Prefab is missing. Deck panel cannot instantiate CardView items.");
            return;
        }

        while (_views.Count < count)
        {
            var go = Instantiate(cardPrefab, cardRoot);
            go.name = $"DeckCardView_{_views.Count:000}";
            if (!go.TryGetComponent(out CardView view))
            {
                Debug.LogError("[RunDeckPanelAnimator] Card Prefab must contain a CardView component.");
                Destroy(go);
                return;
            }

            ResetLayoutDrivenTransform(view.RectTransform);
            _views.Add(view);
        }
    }

    private void ScheduleLayoutRefresh()
    {
        if (_layoutRefreshRoutine != null)
            StopCoroutine(_layoutRefreshRoutine);

        _layoutRefreshRoutine = StartCoroutine(RefreshLayoutNextFrame());
    }

    private IEnumerator RefreshLayoutNextFrame()
    {
        yield return null;
        ForceRefreshLayout();
        _layoutRefreshRoutine = null;
    }

    private void ForceRefreshLayout()
    {
        Canvas.ForceUpdateCanvases();
        if (cardRoot != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(cardRoot);

        if (scrollRect != null && scrollRect.content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);

        if (cardRoot != null && cardRoot.parent is RectTransform parent)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);

        Canvas.ForceUpdateCanvases();
    }

    private static void ResetLayoutDrivenTransform(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.anchoredPosition3D = Vector3.zero;
    }

    private static List<CardSO> LoadDeckCards()
    {
        var deck = GraphHub.Instance?.GetFacade<CardDeckFacade>();
        if (deck == null)
        {
            deck = new CardDeckFacade();
            GraphHub.Instance?.RegisterFacade(deck);
        }

        return deck?.GetCards() ?? new List<CardSO>();
    }

    private static void OnCardHoverEnter(CardView view)
    {
        view?.SetHoverState(true);
    }

    private static void OnCardHoverExit(CardView view)
    {
        view?.SetHoverState(false);
    }
}
