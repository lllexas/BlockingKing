using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class CardView : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    [Header("View")]
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Graphic raycastGraphic;
    [SerializeField] private RectTransform upperRoot;
    [SerializeField] private Image artImage;
    [SerializeField] private Sprite fallbackArtSprite;
    [SerializeField] private Image frameImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text instanceText;
    [SerializeField] private float upperHoverOffsetY = 25f;
    [SerializeField] private RectTransform hoverHoldArea;

    private Sequence _motion;
    private Sequence _layoutMotion;
    private Action<CardView> _clickHandler;
    private Action<CardView> _hoverEnterHandler;
    private Action<CardView> _hoverExitHandler;
    private Action<CardView, PointerEventData> _beginDragHandler;
    private Action<CardView, PointerEventData> _dragHandler;
    private Action<CardView, PointerEventData> _endDragHandler;
    private bool _interactable = true;
    private Vector2 _baseSize = new Vector2(180f, 240f);
    private Vector2 _hoverSize = new Vector2(180f, 290f);
    private Vector2 _upperBaseAnchoredPosition;
    private Vector2 _upperBaseSize;

    public CardSO Card { get; private set; }

    public RectTransform RectTransform => rectTransform != null ? rectTransform : (rectTransform = GetComponent<RectTransform>());

    public void SetLayoutSizes(Vector2 baseSize, Vector2 hoverSize)
    {
        _baseSize = baseSize;
        _hoverSize = hoverSize;
        if (upperRoot != null)
        {
            _upperBaseAnchoredPosition = upperRoot.anchoredPosition;
            _upperBaseSize = upperRoot.sizeDelta;
        }

        ApplyLayout(false);
    }

    private void Awake()
    {
        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (raycastGraphic == null)
            raycastGraphic = GetComponent<Graphic>();

        if (raycastGraphic == null)
        {
            var image = gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f);
            raycastGraphic = image;
        }

        raycastGraphic.raycastTarget = true;

        if (upperRoot != null)
        {
            _upperBaseAnchoredPosition = upperRoot.anchoredPosition;
            _upperBaseSize = upperRoot.sizeDelta;
        }

        SetHoverHoldAreaActive(false);
    }

    private void OnDestroy()
    {
        _motion?.Kill();
        _layoutMotion?.Kill();
    }

    public void Bind(
        CardSO card,
        Action<CardView> onClicked,
        Action<CardView> onHoverEnter = null,
        Action<CardView> onHoverExit = null,
        Action<CardView, PointerEventData> onBeginDrag = null,
        Action<CardView, PointerEventData> onDrag = null,
        Action<CardView, PointerEventData> onEndDrag = null)
    {
        Card = card;
        _clickHandler = onClicked;
        _hoverEnterHandler = onHoverEnter;
        _hoverExitHandler = onHoverExit;
        _beginDragHandler = onBeginDrag;
        _dragHandler = onDrag;
        _endDragHandler = onEndDrag;

        string displayName = card == null ? "Card" : (!string.IsNullOrWhiteSpace(card.displayName) ? card.displayName : card.cardId);
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "Card";

        if (titleText != null)
            titleText.text = displayName;

        if (costText != null)
            costText.text = card != null ? card.cost.ToString() : string.Empty;

        if (descriptionText != null)
            descriptionText.text = card != null ? card.description ?? string.Empty : string.Empty;

        if (instanceText != null)
            instanceText.text = card != null ? card.instanceId ?? string.Empty : string.Empty;

        if (artImage != null)
        {
            artImage.sprite = card != null && card.icon != null ? card.icon : fallbackArtSprite;
            artImage.enabled = true;
        }

        if (frameImage != null && card != null)
            frameImage.color = Color.white;

        SetInteractable(true);
    }

    public void SetInteractable(bool interactable)
    {
        _interactable = interactable;

        if (canvasGroup != null)
        {
            canvasGroup.interactable = interactable;
            canvasGroup.blocksRaycasts = interactable;
        }
    }

    public void Snap(Vector2 anchoredPosition, float scale, float rotationZ)
    {
        _motion?.Kill();
        RectTransform.anchoredPosition = anchoredPosition;
        RectTransform.localScale = Vector3.one * scale;
        RectTransform.localEulerAngles = new Vector3(0f, 0f, rotationZ);
    }

    public void TweenTo(Vector2 anchoredPosition, float scale, float rotationZ, float duration, Ease ease, Action onComplete = null)
    {
        _motion?.Kill();
        _motion = DOTween.Sequence();
        _motion.Join(RectTransform.DOAnchorPos(anchoredPosition, duration).SetEase(ease));
        _motion.Join(RectTransform.DOScale(scale, duration).SetEase(ease));
        _motion.Join(RectTransform.DOLocalRotate(new Vector3(0f, 0f, rotationZ), duration).SetEase(ease));

        if (onComplete != null)
            _motion.OnComplete(() => onComplete());
    }

    public void SetHoverState(bool hovered, float duration = 0.12f, Ease ease = Ease.OutCubic, Action onComplete = null)
    {
        _layoutMotion?.Kill();
        _layoutMotion = DOTween.Sequence();
        _layoutMotion.Join(upperRoot != null
            ? upperRoot.DOSizeDelta(GetUpperSize(hovered), duration).SetEase(ease)
            : RectTransform.DOSizeDelta(hovered ? _hoverSize : _baseSize, duration).SetEase(ease));
        if (upperRoot != null)
            _layoutMotion.Join(upperRoot.DOAnchorPos(GetUpperPosition(hovered), duration).SetEase(ease));

        if (onComplete != null)
            _layoutMotion.OnComplete(() => onComplete());
    }

    public void SetHoverHoldAreaActive(bool active)
    {
        if (hoverHoldArea != null)
            hoverHoldArea.gameObject.SetActive(active);
    }

    private void ApplyLayout(bool hovered)
    {
        RectTransform.sizeDelta = hovered ? _hoverSize : _baseSize;
        if (upperRoot != null)
        {
            upperRoot.sizeDelta = GetUpperSize(hovered);
            upperRoot.anchoredPosition = GetUpperPosition(hovered);
        }
    }

    private Vector2 GetUpperSize(bool hovered)
    {
        float extraHeight = Mathf.Max(0f, _hoverSize.y - _baseSize.y);
        return _upperBaseSize + new Vector2(0f, hovered ? extraHeight : 0f);
    }

    private Vector2 GetUpperPosition(bool hovered)
    {
        return _upperBaseAnchoredPosition + new Vector2(0f, hovered ? upperHoverOffsetY : 0f);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_interactable)
            return;

        _hoverEnterHandler?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!_interactable)
            return;

        _hoverExitHandler?.Invoke(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_interactable || eventData.button != PointerEventData.InputButton.Left)
            return;

        _clickHandler?.Invoke(this);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!_interactable || eventData.button != PointerEventData.InputButton.Left)
            return;

        _beginDragHandler?.Invoke(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_interactable)
            return;

        _dragHandler?.Invoke(this, eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_interactable || eventData.button != PointerEventData.InputButton.Left)
            return;

        _endDragHandler?.Invoke(this, eventData);
    }
}
