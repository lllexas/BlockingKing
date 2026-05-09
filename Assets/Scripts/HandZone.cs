using System;
using System.Collections.Generic;
using DG.Tweening;
using NekoGraph;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class HandZone : MonoBehaviour
{
    private const string CardReleaseRangeOverlayId = "card_release_range";
    private const string CardReleaseHoverOverlayId = "card_release_hover";
    private const string CardReleaseInvalidOverlayId = "card_release_invalid";

    [Header("References")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private RectTransform cardLayer;
    [SerializeField] private RectTransform handAnchor;
    [SerializeField] private RectTransform drawPileAnchor;
    [SerializeField] private RectTransform discardPileAnchor;
    [SerializeField] private CardView cardPrefab;

    [Header("Layout")]
    [SerializeField] private Vector2 cardSize = new Vector2(180f, 240f);
    [SerializeField] private Vector2 hoverCardSize = new Vector2(180f, 290f);
    [SerializeField] private float handWidthRatio = 0.9f;
    [SerializeField] private float handBottomPadding = 34f;
    [SerializeField] private float preferredSpacing = 320f;
    [SerializeField] private float minSpacing = 125f;
    [SerializeField] private float maxFanAngle = 8f;
    [SerializeField] private float fanArcDepth = 56f;
    [SerializeField] private float hoverLift = 150f;
    [SerializeField] private float cardTweenDuration = 0.22f;
    [SerializeField] private float hoverTweenDuration = 0.12f;
    [SerializeField] private float pileTweenDuration = 0.18f;
    [SerializeField] private Ease cardEase = Ease.OutCubic;
    [SerializeField] private Ease hoverEase = Ease.OutCubic;
    [SerializeField] private Ease pileEase = Ease.InCubic;
    [SerializeField] private bool collapseBeforeDiscardMove = true;

    [Header("Deck Flow")]
    [SerializeField] private CardHandState handState = new CardHandState();
    [SerializeField] private bool fillHandOnStart = true;
    [SerializeField] private bool autoBuildFromLibrary = true;
    [SerializeField] private bool requireLevelForAutoBuild = true;

    [Header("Counters")]
    [SerializeField] private TMP_Text drawPileCountText;
    [SerializeField] private TMP_Text discardPileCountText;
    [SerializeField] private TMP_Text handCountText;

    [Header("Grid Overlay")]
    [SerializeField] private Color cardReleaseRangeColor = new Color(0.2f, 0.75f, 1f, 0.28f);
    [SerializeField] private Color cardReleaseHoverColor = new Color(0.35f, 1f, 0.55f, 0.58f);
    [SerializeField] private Color cardReleaseInvalidColor = new Color(1f, 0.1f, 0.05f, 0.48f);
    [SerializeField] private float cardReleaseOverlayHeight = 0.012f;

    public event Action<CardSO> CardPlayed;
    public static bool IsAnyCardAiming { get; private set; }

    private readonly List<CardSO> _drawPile = new List<CardSO>();
    private readonly List<CardSO> _discardPile = new List<CardSO>();
    private readonly List<CardView> _hand = new List<CardView>();
    private readonly List<Vector2Int> _cardReleaseCells = new List<Vector2Int>();
    private readonly List<Vector2Int> _cardReleaseHoverCells = new List<Vector2Int>();

    private int _cachedScreenWidth;
    private int _cachedScreenHeight;
    private int _observedHandStateRevision = -1;
    private bool _initialized;
    private bool _waitingForLevel;
    private CardView _hoveredCard;
    private CardView _draggingCard;
    private CardView _pendingCard;
    private CardView _stateCard;
    private CardInteractionState _interactionState = CardInteractionState.Idle;
    private int _pendingCardIndex = -1;
    private EntityHandle _playerHandle = EntityHandle.None;
    private Vector2 _cachedCardSize;
    private Vector2 _cachedHoverCardSize;
    private float _cachedHandWidthRatio;
    private float _cachedHandBottomPadding;
    private float _cachedPreferredSpacing;
    private float _cachedMinSpacing;
    private float _cachedMaxFanAngle;
    private float _cachedFanArcDepth;
    private float _cachedHoverLift;

    public CardHandState State => handState;

    private enum CardInteractionState
    {
        Idle,
        Hovered,
        Dragging,
        Pending,
        Recycling
    }

    private readonly struct HandSlot
    {
        public readonly Vector2 Position;
        public readonly float Rotation;
        public readonly float Scale;

        public HandSlot(Vector2 position, float rotation, float scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
    }

    private void Awake()
    {
        handState ??= new CardHandState();

        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>();

        if (cardLayer == null)
            cardLayer = transform as RectTransform;

        CacheLayoutSettings();
    }

    private void Start()
    {
        if (!autoBuildFromLibrary)
            return;

        if (CanAutoBuildNow())
        {
            RebuildFromLibrary(fillHandOnStart);
            return;
        }

        _waitingForLevel = true;
    }

    private void OnDisable()
    {
        if (_interactionState == CardInteractionState.Pending)
            IsAnyCardAiming = false;

        ClearCardReleaseOverlay();
    }

    private void Update()
    {
        if (_waitingForLevel && CanAutoBuildNow())
        {
            _waitingForLevel = false;
            RebuildFromLibrary(fillHandOnStart);
        }

        if (!_initialized)
            return;

        if (_cachedScreenWidth != Screen.width || _cachedScreenHeight != Screen.height)
        {
            _cachedScreenWidth = Screen.width;
            _cachedScreenHeight = Screen.height;
            RelayoutHand(false);
        }

        if (HasLayoutSettingsChanged())
        {
            bool cardSizeChanged = HasCardSizeSettingsChanged();
            CacheLayoutSettings();
            if (cardSizeChanged)
                ApplyCardLayoutSizes();
            RelayoutHand(false);
        }

        PollHandState();

        if (_pendingCard != null)
        {
            UpdateCardReleaseHoverOverlay();
            PollPendingCardAim();
        }
    }

    private void OnGUI()
    {
        if (_pendingCard == null)
            return;

        var rect = new Rect(Screen.width * 0.5f - 220f, 24f, 440f, 36f);
        GUI.Box(rect, "左键确认方向 / 右键取消");
    }

    public void RebuildFromLibrary(bool fillToMax)
    {
        var deck = EnsureDeckFacade();
        if (deck == null)
            return;

        ClearRuntimeCards();
        _drawPile.Clear();
        _discardPile.Clear();
        ApplyRunStartHandSettings();

        var cards = deck.GetCards();
        if (cards != null)
            _drawPile.AddRange(cards);

        Shuffle(_drawPile);
        _initialized = true;
        _cachedScreenWidth = Screen.width;
        _cachedScreenHeight = Screen.height;

        if (fillToMax)
            RefillHandToMax(true);
        else
            DrawCards(handState.TargetHandCount, true);

        _observedHandStateRevision = handState.Revision;
        RefreshCounters();
    }

    private void ApplyRunStartHandSettings()
    {
        var startSettings = GameFlowController.Instance?.RunStartSettings;
        if (startSettings == null)
            return;

        handState.SetMaxHandCount(startSettings.maxHandCount);
        handState.SetTargetHandCount(startSettings.targetHandCount);
        handState.SetAutoRefill(startSettings.autoRefill);
    }

    public bool DrawCard()
    {
        return DrawCards(1, true) > 0;
    }

    public int DrawCards(int count, bool animate)
    {
        if (count <= 0)
            return 0;

        if (cardPrefab == null || cardLayer == null)
        {
            Debug.LogWarning("[HandZone] Card prefab or card layer is missing.");
            return 0;
        }

        int drawn = 0;
        for (int i = 0; i < count && _hand.Count < handState.MaxHandCount; i++)
        {
            if (!EnsureDrawPileHasCards())
                break;

            var card = PopTopDrawCard();
            if (card == null)
                break;

            if (CreateHandCard(card, animate))
                drawn++;
        }

        if (drawn > 0)
            RelayoutHand(animate);

        RefreshCounters();
        return drawn;
    }

    public bool TryPlayCard(CardView view, CardReleaseTarget target)
    {
        if (view == null)
            return false;

        int index = _hand.IndexOf(view);
        if (index < 0)
            return false;

        var card = view.Card;
        if (card == null)
            return false;

        if (!TryResolvePlayer(out var playerHandle))
            return false;

        var intentSystem = IntentSystem.Instance;
        if (intentSystem == null)
            return false;

        var intent = intentSystem.Request<CardIntent>();
        intent.Setup(card, target);

        if (!intentSystem.SetPlayerIntent(playerHandle, IntentType.Card, intent))
        {
            intentSystem.Return(intent);
            return false;
        }

        ChangeInteractionState(CardInteractionState.Recycling, view);
        _hand.RemoveAt(index);

        _discardPile.Add(card);
        CardPlayed?.Invoke(card);

        StartDiscardRecycle(view);
        ChangeInteractionState(CardInteractionState.Idle, null);

        TickSystem.PushTick();

        if (handState.AutoRefill)
            RefillHandToTarget(true);
        else
            RelayoutHand(true);

        RefreshCounters();
        return true;
    }

    public void RefillHandToMax(bool animate)
    {
        RefillHandToTarget(animate);
    }

    public void RefillHandToTarget(bool animate)
    {
        int targetCount = Mathf.Min(handState.TargetHandCount, handState.MaxHandCount);
        if (_hand.Count >= targetCount)
        {
            RefreshCounters();
            return;
        }

        DrawCards(targetCount - _hand.Count, animate);
    }

    public int HandCount => _hand.Count;
    public int DrawPileCount => _drawPile.Count;
    public int DiscardPileCount => _discardPile.Count;

    private bool CanAutoBuildNow()
    {
        if (!requireLevelForAutoBuild)
            return true;

        var flow = GameFlowController.Instance;
        return flow == null || flow.IsInLevel;
    }

    private bool CreateHandCard(CardSO card, bool animate)
    {
        if (cardPrefab == null || cardLayer == null)
            return false;

        var view = Instantiate(cardPrefab, cardLayer);
        view.transform.SetAsLastSibling();
        view.SetLayoutSizes(cardSize, hoverCardSize);
        view.Bind(
            card,
            HandleCardClicked,
            HandleCardHoverEnter,
            HandleCardHoverExit,
            HandleCardBeginDrag,
            HandleCardDrag,
            HandleCardEndDrag);
        view.SetInteractable(false);

        var spawnPosition = GetAnchorLocalPosition(drawPileAnchor, DefaultDrawFallback());
        view.Snap(spawnPosition, 0.82f, 0f);
        _hand.Add(view);

        if (!animate)
        {
            view.Snap(view.RectTransform.anchoredPosition, 1f, 0f);
            view.SetInteractable(true);
        }

        return true;
    }

    private void HandleCardClicked(CardView view)
    {
        // 点击不再直接打出。卡牌必须拖出 HandZone 后进入瞄准态。
    }

    private void HandleCardHoverEnter(CardView view)
    {
        if (view == null || !_hand.Contains(view) || _interactionState != CardInteractionState.Idle)
            return;

        ChangeInteractionState(CardInteractionState.Hovered, view);
    }

    private void HandleCardHoverExit(CardView view)
    {
        if (view == null || _interactionState != CardInteractionState.Hovered || _stateCard != view)
            return;

        ChangeInteractionState(CardInteractionState.Idle, null);
    }

    private void HandleCardBeginDrag(CardView view, PointerEventData eventData)
    {
        if (view == null || !_hand.Contains(view))
            return;

        if (_interactionState != CardInteractionState.Idle && !(_interactionState == CardInteractionState.Hovered && _stateCard == view))
            return;

        ChangeInteractionState(CardInteractionState.Dragging, view);
        MoveCardToPointer(view, eventData);
    }

    private void HandleCardDrag(CardView view, PointerEventData eventData)
    {
        if (view == null || _draggingCard != view)
            return;

        MoveCardToPointer(view, eventData);
    }

    private void HandleCardEndDrag(CardView view, PointerEventData eventData)
    {
        if (view == null || _interactionState != CardInteractionState.Dragging || _stateCard != view)
            return;

        MoveCardToPointer(view, eventData);

        if (IsPointerInsideHandZone(eventData))
        {
            ChangeInteractionState(CardInteractionState.Idle, null);
            RelayoutHand(true);
            return;
        }

        ChangeInteractionState(CardInteractionState.Pending, view);
    }

    private void ChangeInteractionState(CardInteractionState nextState, CardView nextCard)
    {
        if (_interactionState == nextState && _stateCard == nextCard)
            return;

        ExitInteractionState(_interactionState, _stateCard, nextState);
        _interactionState = nextState;
        _stateCard = nextCard;
        EnterInteractionState(nextState, nextCard);
    }

    private void EnterInteractionState(CardInteractionState state, CardView view)
    {
        switch (state)
        {
            case CardInteractionState.Idle:
                break;

            case CardInteractionState.Hovered:
                EnterHoveredState(view);
                break;

            case CardInteractionState.Dragging:
                EnterDraggingState(view);
                break;

            case CardInteractionState.Pending:
                EnterPendingState(view);
                break;

            case CardInteractionState.Recycling:
                EnterRecyclingState(view);
                break;
        }
    }

    private void ExitInteractionState(CardInteractionState state, CardView view, CardInteractionState nextState)
    {
        switch (state)
        {
            case CardInteractionState.Idle:
                break;

            case CardInteractionState.Hovered:
                ExitHoveredState(view, nextState);
                break;

            case CardInteractionState.Dragging:
                ExitDraggingState(view, nextState);
                break;

            case CardInteractionState.Pending:
                IsAnyCardAiming = false;
                ClearCardReleaseOverlay();
                if (_pendingCard == view)
                    _pendingCard = null;
                _pendingCardIndex = -1;
                break;

            case CardInteractionState.Recycling:
                break;
        }
    }

    private void EnterHoveredState(CardView view)
    {
        if (view == null)
            return;

        _hoveredCard = view;
        view.transform.SetAsLastSibling();

        int index = _hand.IndexOf(view);
        if (index < 0)
            return;

        var slot = BuildHandSlots(_hand.Count)[index];
        view.SetHoverHoldAreaActive(true);
        view.SetHoverState(true, hoverTweenDuration, hoverEase);
        view.TweenTo(
            slot.Position + new Vector2(0f, hoverLift),
            slot.Scale,
            0f,
            hoverTweenDuration,
            hoverEase);
    }

    private void ExitHoveredState(CardView view, CardInteractionState nextState)
    {
        if (_hoveredCard == view)
            _hoveredCard = null;

        if (view != null)
            view.SetHoverHoldAreaActive(false);

        if (view == null || nextState == CardInteractionState.Dragging || nextState == CardInteractionState.Pending || nextState == CardInteractionState.Recycling)
            return;

        view.SetHoverState(false, hoverTweenDuration, hoverEase);
        RestoreSingleCardSlot(view, true);
    }

    private void EnterDraggingState(CardView view)
    {
        if (view == null)
            return;

        view.SetHoverHoldAreaActive(false);
        _draggingCard = view;
        view.transform.SetAsLastSibling();
    }

    private void ExitDraggingState(CardView view, CardInteractionState nextState)
    {
        if (_draggingCard == view)
            _draggingCard = null;

        if (view == null || nextState != CardInteractionState.Idle)
            return;

        view.SetHoverState(false, hoverTweenDuration, hoverEase);
    }

    private void EnterPendingState(CardView view)
    {
        if (view == null)
            return;

        int index = _hand.IndexOf(view);
        if (index < 0)
        {
            ChangeInteractionState(CardInteractionState.Idle, null);
            RelayoutHand(true);
            return;
        }

        _pendingCard = view;
        _pendingCardIndex = index;
        IsAnyCardAiming = true;
        view.SetHoverHoldAreaActive(false);
        view.transform.SetAsLastSibling();
        view.SetHoverState(true, hoverTweenDuration, hoverEase);
        BuildCardReleaseRangeOverlay(view);
    }

    private void EnterRecyclingState(CardView view)
    {
        if (view == null)
            return;

        if (_hoveredCard == view)
            _hoveredCard = null;

        if (_draggingCard == view)
            _draggingCard = null;

        if (_pendingCard == view)
            _pendingCard = null;

        _pendingCardIndex = -1;
        view.SetHoverHoldAreaActive(false);
        view.SetInteractable(false);
        IsAnyCardAiming = false;
        ClearCardReleaseOverlay();
    }

    private void StartDiscardRecycle(CardView view)
    {
        if (view == null)
            return;

        if (collapseBeforeDiscardMove)
        {
            view.SetHoverState(false, hoverTweenDuration, hoverEase, () => MoveCardToDiscardPile(view));
            return;
        }

        view.SetHoverState(false, hoverTweenDuration, hoverEase);
        MoveCardToDiscardPile(view);
    }

    private void MoveCardToDiscardPile(CardView view)
    {
        if (view == null)
            return;

        view.TweenTo(GetAnchorLocalPosition(discardPileAnchor, DefaultDiscardFallback()), 0.9f, 0f, pileTweenDuration, pileEase, () =>
        {
            if (view != null)
                Destroy(view.gameObject);
        });
    }

    private void PollPendingCardAim()
    {
        if (Input.GetMouseButtonDown(1))
        {
            CancelPendingCard();
            return;
        }

        if (!Input.GetMouseButtonDown(0))
            return;

        var pending = _pendingCard;
        if (!TryResolveCardReleaseTarget(pending, out var target))
            return;

        ChangeInteractionState(CardInteractionState.Recycling, pending);

        if (!TryPlayCard(pending, target))
        {
            ChangeInteractionState(CardInteractionState.Pending, pending);
            CancelPendingCard();
        }
    }

    private void CancelPendingCard()
    {
        var pending = _pendingCard;
        int index = _pendingCardIndex;
        ChangeInteractionState(CardInteractionState.Idle, null);

        if (pending == null)
        {
            RelayoutHand(true);
            return;
        }

        if (!_hand.Contains(pending))
            _hand.Insert(Mathf.Clamp(index, 0, _hand.Count), pending);

        pending.SetInteractable(true);
        pending.SetHoverState(false, hoverTweenDuration, hoverEase);
        RelayoutHand(true);
    }

    private void PollHandState()
    {
        if (handState == null)
            handState = new CardHandState();

        if (_observedHandStateRevision == handState.Revision)
            return;

        _observedHandStateRevision = handState.Revision;
        ApplyHandState(true);
    }

    private void ApplyHandState(bool animate)
    {
        int targetCount = Mathf.Min(handState.TargetHandCount, handState.MaxHandCount);

        while (_hand.Count > handState.MaxHandCount)
        {
            int lastIndex = _hand.Count - 1;
            var view = _hand[lastIndex];
            _hand.RemoveAt(lastIndex);

            if (view != null && view.Card != null)
                _discardPile.Add(view.Card);

            if (view != null)
                Destroy(view.gameObject);
        }

        if (handState.AutoRefill && _hand.Count < targetCount)
            DrawCards(targetCount - _hand.Count, animate);
        else
            RelayoutHand(animate);

        RefreshCounters();
    }

    private void RelayoutHand(bool animate)
    {
        if (_hand.Count == 0)
        {
            RefreshCounters();
            return;
        }

        var slots = BuildHandSlots(_hand.Count);
        for (int i = 0; i < _hand.Count; i++)
        {
            var view = _hand[i];
            if (view == null)
                continue;

            if (_hoveredCard == view)
                continue;

            if (_draggingCard == view || _pendingCard == view)
                continue;

            view.transform.SetSiblingIndex(i);
            var slot = slots[i];
            if (animate)
            {
                view.SetInteractable(true);
                view.TweenTo(slot.Position, slot.Scale, slot.Rotation, cardTweenDuration, cardEase);
            }
            else
            {
                view.Snap(slot.Position, slot.Scale, slot.Rotation);
                view.SetInteractable(true);
            }
        }

        RefreshCounters();
    }

    private void ApplyCardLayoutSizes()
    {
        for (int i = 0; i < _hand.Count; i++)
        {
            var view = _hand[i];
            if (view != null)
            {
                view.SetLayoutSizes(cardSize, hoverCardSize);
            }
        }
    }

    private void CacheLayoutSettings()
    {
        _cachedCardSize = cardSize;
        _cachedHoverCardSize = hoverCardSize;
        _cachedHandWidthRatio = handWidthRatio;
        _cachedHandBottomPadding = handBottomPadding;
        _cachedPreferredSpacing = preferredSpacing;
        _cachedMinSpacing = minSpacing;
        _cachedMaxFanAngle = maxFanAngle;
        _cachedFanArcDepth = fanArcDepth;
        _cachedHoverLift = hoverLift;
    }

    private bool HasLayoutSettingsChanged()
    {
        return _cachedCardSize != cardSize
            || _cachedHoverCardSize != hoverCardSize
            || !Mathf.Approximately(_cachedHandWidthRatio, handWidthRatio)
            || !Mathf.Approximately(_cachedHandBottomPadding, handBottomPadding)
            || !Mathf.Approximately(_cachedPreferredSpacing, preferredSpacing)
            || !Mathf.Approximately(_cachedMinSpacing, minSpacing)
            || !Mathf.Approximately(_cachedMaxFanAngle, maxFanAngle)
            || !Mathf.Approximately(_cachedFanArcDepth, fanArcDepth)
            || !Mathf.Approximately(_cachedHoverLift, hoverLift);
    }

    private bool HasCardSizeSettingsChanged()
    {
        return _cachedCardSize != cardSize || _cachedHoverCardSize != hoverCardSize;
    }

    private void RestoreSingleCardSlot(CardView view, bool animate)
    {
        if (view == null)
            return;

        int index = _hand.IndexOf(view);
        if (index < 0)
            return;

        var slot = BuildHandSlots(_hand.Count)[index];
        if (animate)
            view.TweenTo(slot.Position, slot.Scale, slot.Rotation, hoverTweenDuration, hoverEase);
        else
            view.Snap(slot.Position, slot.Scale, slot.Rotation);
    }

    private HandSlot[] BuildHandSlots(int count)
    {
        var slots = new HandSlot[count];
        if (count <= 0)
            return slots;

        float availableWidth = Mathf.Max(cardSize.x, GetHandAreaWidth());
        float spacing = preferredSpacing;

        if (count > 1)
        {
            float preferredWidth = cardSize.x + preferredSpacing * (count - 1);
            if (preferredWidth > availableWidth)
                spacing = Mathf.Max(minSpacing, (availableWidth - cardSize.x) / (count - 1));
        }

        Vector2 center = GetHandCenter();
        float span = spacing * (count - 1);

        for (int i = 0; i < count; i++)
        {
            float t = count <= 1 ? 0f : ((float)i / (count - 1)) * 2f - 1f;
            float x = center.x + t * span * 0.5f;
            float arc = fanArcDepth * (1f - t * t);
            float y = center.y + arc;
            float rotation = -maxFanAngle * t;
            slots[i] = new HandSlot(
                new Vector2(x, y),
                rotation,
                1f);
        }

        return slots;
    }

    private Vector2 GetHandCenter()
    {
        if (handAnchor != null)
            return GetLocalPoint(handAnchor.position);

        Rect rect = cardLayer != null ? cardLayer.rect : new Rect(0f, 0f, Screen.width, Screen.height);
        return new Vector2((rect.xMin + rect.xMax) * 0.5f, rect.yMin + handBottomPadding + cardSize.y * 0.5f);
    }

    private float GetHandAreaWidth()
    {
        Rect rect = cardLayer != null ? cardLayer.rect : new Rect(0f, 0f, Screen.width, Screen.height);
        return rect.width * Mathf.Clamp01(handWidthRatio);
    }

    private Vector2 GetLocalPoint(Vector3 worldPosition)
    {
        if (cardLayer == null)
            return Vector2.zero;

        Camera cam = GetCanvasCamera();
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(cardLayer, screenPoint, cam, out var localPoint);
        return localPoint;
    }

    private Vector2 GetAnchorLocalPosition(RectTransform anchor, Vector2 fallback)
    {
        if (anchor != null)
            return GetLocalPoint(anchor.position);

        return fallback;
    }

    private Vector2 DefaultDrawFallback()
    {
        Rect rect = cardLayer != null ? cardLayer.rect : new Rect(0f, 0f, Screen.width, Screen.height);
        return new Vector2(rect.xMin + cardSize.x * 0.5f + 60f, rect.yMin + cardSize.y * 0.5f + 60f);
    }

    private Vector2 DefaultDiscardFallback()
    {
        Rect rect = cardLayer != null ? cardLayer.rect : new Rect(0f, 0f, Screen.width, Screen.height);
        return new Vector2(rect.xMax - cardSize.x * 0.5f - 60f, rect.yMin + cardSize.y * 0.5f + 60f);
    }

    private bool EnsureDrawPileHasCards()
    {
        if (_drawPile.Count > 0)
            return true;

        if (_discardPile.Count == 0)
            return false;

        _drawPile.AddRange(_discardPile);
        _discardPile.Clear();
        Shuffle(_drawPile);
        RefreshCounters();
        return _drawPile.Count > 0;
    }

    private CardSO PopTopDrawCard()
    {
        if (_drawPile.Count == 0)
            return null;

        var card = _drawPile[0];
        _drawPile.RemoveAt(0);
        return card;
    }

    private void Shuffle(List<CardSO> cards)
    {
        if (cards == null)
            return;

        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            var temp = cards[i];
            cards[i] = cards[j];
            cards[j] = temp;
        }
    }

    private void ClearRuntimeCards()
    {
        _interactionState = CardInteractionState.Idle;
        _stateCard = null;
        _hoveredCard = null;
        _draggingCard = null;
        _pendingCard = null;
        _pendingCardIndex = -1;
        IsAnyCardAiming = false;
        ClearCardReleaseOverlay();

        if (cardLayer == null)
            return;

        for (int i = cardLayer.childCount - 1; i >= 0; i--)
        {
            var child = cardLayer.GetChild(i);
            if (child != null && child.GetComponent<CardView>() != null)
                Destroy(child.gameObject);
        }

        _hand.Clear();
    }

    private void MoveCardToPointer(CardView view, PointerEventData eventData)
    {
        if (view == null || cardLayer == null || eventData == null)
            return;

        if (!TryGetPointerLocalPoint(eventData, out var localPoint))
            return;

        view.Snap(localPoint, 1f, 0f);
    }

    private bool IsPointerInsideHandZone(PointerEventData eventData)
    {
        if (eventData == null || !TryGetPointerLocalPoint(eventData, out var localPoint))
            return false;

        return GetHandZoneRect().Contains(localPoint);
    }

    private bool TryGetPointerLocalPoint(PointerEventData eventData, out Vector2 localPoint)
    {
        if (cardLayer == null || eventData == null)
        {
            localPoint = default;
            return false;
        }

        var camera = eventData.pressEventCamera != null ? eventData.pressEventCamera : GetCanvasCamera();
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            cardLayer,
            eventData.position,
            camera,
            out localPoint);
    }

    private Rect GetHandZoneRect()
    {
        Vector2 center = GetHandCenter();
        float width = GetHandAreaWidth();
        float height = cardSize.y + hoverLift + 40f;
        return new Rect(center.x - width * 0.5f, center.y - cardSize.y * 0.5f, width, height);
    }

    private bool TryResolveCardReleaseTarget(CardView view, out CardReleaseTarget target)
    {
        target = default;
        if (view == null || view.Card == null)
            return false;

        if (!TryResolvePlayer(out var playerHandle))
            return false;

        if (CardReleaseRuleRegistry.TryResolveWithoutSelectedCell(view.Card, playerHandle, out target))
            return true;

        if (!TryGetMouseGridPosition(out var targetPosition))
            return false;

        return CardReleaseRuleRegistry.TryResolve(view.Card, playerHandle, targetPosition, out target);
    }

    private void BuildCardReleaseRangeOverlay(CardView view)
    {
        ClearCardReleaseOverlay();

        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null || view == null || view.Card == null)
            return;

        if (!TryResolvePlayer(out var playerHandle))
            return;

        CardReleaseRuleRegistry.CollectCandidates(view.Card, playerHandle, _cardReleaseCells);
        overlay.SetOverlay(
            CardReleaseRangeOverlayId,
            _cardReleaseCells,
            GridOverlayStyle.SolidTint,
            cardReleaseRangeColor,
            cardReleaseOverlayHeight,
            10);
    }

    private void UpdateCardReleaseHoverOverlay()
    {
        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.RemoveOverlay(CardReleaseHoverOverlayId);
        overlay.RemoveOverlay(CardReleaseInvalidOverlayId);
        _cardReleaseHoverCells.Clear();

        var pending = _pendingCard;
        if (pending == null || pending.Card == null)
            return;

        if (CardReleaseRuleRegistry.RequiresSelectedCell(pending.Card.releaseRule))
        {
            if (!TryGetMouseGridPosition(out var targetPosition))
                return;

            var entitySystem = EntitySystem.Instance;
            if (entitySystem != null && entitySystem.IsInitialized && !entitySystem.IsInsideMap(targetPosition))
                return;

            _cardReleaseHoverCells.Add(targetPosition);
            if (TryResolveCardReleaseTarget(pending, out _))
            {
                overlay.SetOverlay(
                    CardReleaseHoverOverlayId,
                    _cardReleaseHoverCells,
                    GridOverlayStyle.SoftGlow,
                    cardReleaseHoverColor,
                    cardReleaseOverlayHeight,
                    11);
            }
            else
            {
                overlay.SetOverlay(
                    CardReleaseInvalidOverlayId,
                    _cardReleaseHoverCells,
                    GridOverlayStyle.InvalidTarget,
                    cardReleaseInvalidColor,
                    cardReleaseOverlayHeight,
                    12);
            }

            return;
        }

        if (!TryResolveCardReleaseTarget(pending, out var selfTarget))
            return;

        _cardReleaseHoverCells.Add(selfTarget.TargetCell);
        overlay.SetOverlay(
            CardReleaseHoverOverlayId,
            _cardReleaseHoverCells,
            GridOverlayStyle.SelectionRing,
            cardReleaseHoverColor,
            cardReleaseOverlayHeight,
            11);
    }

    private void ClearCardReleaseOverlay()
    {
        var overlay = GridOverlayDrawSystem.Instance;
        if (overlay == null)
            return;

        overlay.RemoveOverlay(CardReleaseRangeOverlayId);
        overlay.RemoveOverlay(CardReleaseHoverOverlayId);
        overlay.RemoveOverlay(CardReleaseInvalidOverlayId);
        _cardReleaseCells.Clear();
        _cardReleaseHoverCells.Clear();
    }

    private bool TryGetMouseGridPosition(out Vector2Int gridPosition)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            gridPosition = default;
            return false;
        }

        var ray = camera.ScreenPointToRay(Input.mousePosition);
        var floorPlane = new Plane(Vector3.up, Vector3.zero);
        if (!floorPlane.Raycast(ray, out float distance))
        {
            gridPosition = default;
            return false;
        }

        Vector3 world = ray.GetPoint(distance);
        gridPosition = new Vector2Int(Mathf.FloorToInt(world.x), Mathf.FloorToInt(world.z));
        return true;
    }

    private CardDeckFacade EnsureDeckFacade()
    {
        var deck = GraphHub.Instance?.GetFacade<CardDeckFacade>();
        if (deck != null)
            return deck;

        deck = new CardDeckFacade();
        GraphHub.Instance?.RegisterFacade(deck);
        return deck;
    }

    private bool TryResolvePlayer(out EntityHandle playerHandle)
    {
        if (EntitySystem.Instance == null || !EntitySystem.Instance.IsInitialized)
        {
            playerHandle = EntityHandle.None;
            return false;
        }

        if (EntitySystem.Instance.IsValid(_playerHandle))
        {
            int idx = EntitySystem.Instance.GetIndex(_playerHandle);
            if (idx >= 0 && EntitySystem.Instance.entities.coreComponents[idx].EntityType == EntityType.Player)
            {
                playerHandle = _playerHandle;
                return true;
            }
            // 句柄有效但指向了非玩家实体 → 缓存失效，重新遍历
            _playerHandle = EntityHandle.None;
        }

        var entities = EntitySystem.Instance.entities;
        for (int i = 0; i < entities.entityCount; i++)
        {
            if (entities.coreComponents[i].EntityType != EntityType.Player)
                continue;

            _playerHandle = EntitySystem.Instance.GetHandleFromId(entities.coreComponents[i].Id);
            playerHandle = _playerHandle;
            return true;
        }

        playerHandle = EntityHandle.None;
        return false;
    }

    private Camera GetCanvasCamera()
    {
        if (rootCanvas == null)
            return null;

        return rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
    }

    private void RefreshCounters()
    {
        if (drawPileCountText != null)
            drawPileCountText.text = _drawPile.Count.ToString();

        if (discardPileCountText != null)
            discardPileCountText.text = _discardPile.Count.ToString();

        if (handCountText != null)
            handCountText.text = _hand.Count.ToString();
    }
}
