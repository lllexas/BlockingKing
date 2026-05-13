using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

[DefaultExecutionOrder(120)]
public sealed class RewardCardPresentationAnimator : MonoBehaviour
{
    public static RewardCardPresentationAnimator ActiveInstance { get; private set; }

    [Header("Presentation")]
    [SerializeField] private RectTransform presentationRoot;
    [SerializeField] private GameObject cardPrefab;
    [SerializeField] private RectTransform deckTargetAnchor;
    [SerializeField] private Vector2 cardBaseSize = new(180f, 240f);
    [SerializeField] private Vector2 cardHoverSize = new(180f, 290f);

    [Header("Motion")]
    [SerializeField] private Vector2 startViewportPosition = new(0.5f, 0.5f);
    [SerializeField] private Vector2 holdOffset = new(0f, 24f);
    [SerializeField] private Vector2 fallbackTargetViewportPosition = new(0.92f, 0.08f);
    [SerializeField] private float holdDuration = 1f;
    [SerializeField] private float flyDuration = 0.82f;
    [SerializeField] private Ease ease = Ease.OutCubic;

    [Header("Debug")]
    [SerializeField] private bool debugLogging = true;

    private readonly List<CardView> _spawnedViews = new();
    private Canvas _canvas;

    private void Awake()
    {
        ActiveInstance = this;

        if (presentationRoot == null)
            presentationRoot = transform as RectTransform;

        _canvas = GetComponentInParent<Canvas>();
        ForceVisiblePresentationRoot();
        LogPlayerState("Awake");
    }

    private void OnDestroy()
    {
        if (ActiveInstance == this)
            ActiveInstance = null;

        for (int i = _spawnedViews.Count - 1; i >= 0; i--)
        {
            if (_spawnedViews[i] != null)
                Destroy(_spawnedViews[i].gameObject);
        }

        _spawnedViews.Clear();
    }

    public bool TryPlayAddToDeck(CardSO card, int count = 1)
    {
        if (!ValidatePlayRequest(card, count))
            return false;

        bool anyPlayed = false;
        for (int i = 0; i < count; i++)
        {
            var viewObject = Instantiate(cardPrefab, presentationRoot);
            if (viewObject == null)
            {
                LogError($"Instantiate returned null. card={GetCardId(card)}");
                continue;
            }

            if (!viewObject.TryGetComponent<CardView>(out var view))
            {
                LogError($"Instantiated card prefab has no CardView component. prefab={cardPrefab.name}, instance={GetTransformPath(viewObject.transform)}, card={GetCardId(card)}");
                Destroy(viewObject);
                continue;
            }

            _spawnedViews.Add(view);
            view.transform.SetAsLastSibling();
            view.gameObject.SetActive(true);
            view.SetLayoutSizes(cardBaseSize, cardHoverSize);
            view.Bind(card, null);
            view.SetInteractable(false);
            view.SetHoverState(false, 0f, ease);

            Vector2 start = ViewportToLocal(startViewportPosition);
            Vector2 hold = start + holdOffset;
            Vector2 target = ResolveTargetLocalPosition();

            view.Snap(start, 1f, 0f);
            LogSpawn(card, view, start, hold, target);

            view.TweenTo(hold, 1f, 0f, holdDuration, ease, () =>
            {
                if (view == null)
                    return;

                view.TweenTo(target, 0.82f, 0f, flyDuration, ease, () =>
                {
                    _spawnedViews.Remove(view);
                    if (view != null)
                        Destroy(view.gameObject);
                });
            });

            anyPlayed = true;
        }

        return anyPlayed;
    }

    private bool ValidatePlayRequest(CardSO card, int count)
    {
        if (card == null)
        {
            LogError("Play request rejected: card is null.");
            return false;
        }

        if (count <= 0)
        {
            LogError($"Play request rejected: count={count}. card={GetCardId(card)}");
            return false;
        }

        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            LogError($"Play request rejected: player inactive. player={GetTransformPath(transform)}, activeSelf={gameObject.activeSelf}, activeInHierarchy={gameObject.activeInHierarchy}, enabled={enabled}");
            return false;
        }

        if (presentationRoot == null)
        {
            LogError($"Play request rejected: presentationRoot is null. player={GetTransformPath(transform)}");
            return false;
        }

        if (!presentationRoot.gameObject.activeInHierarchy)
        {
            LogError($"Play request rejected: presentationRoot inactive. root={GetTransformPath(presentationRoot)}, activeSelf={presentationRoot.gameObject.activeSelf}, activeInHierarchy={presentationRoot.gameObject.activeInHierarchy}");
            return false;
        }

        if (cardPrefab == null)
        {
            LogError($"Play request rejected: cardPrefab is null. player={GetTransformPath(transform)}");
            return false;
        }

        if (presentationRoot.rect.width <= 0f || presentationRoot.rect.height <= 0f)
        {
            LogError($"Play request rejected: presentationRoot rect is invalid. root={GetTransformPath(presentationRoot)}, rect={presentationRoot.rect}");
            return false;
        }

        float alpha = GetEffectiveCanvasGroupAlpha(presentationRoot);
        if (alpha <= 0.01f)
        {
            LogError($"Play request rejected: presentationRoot effective alpha is {alpha:0.###}. root={GetTransformPath(presentationRoot)}");
            return false;
        }

        return true;
    }

    private void ForceVisiblePresentationRoot()
    {
        if (presentationRoot == null)
            return;

        if (presentationRoot.TryGetComponent<CanvasGroup>(out var canvasGroup))
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    private Vector2 ResolveTargetLocalPosition()
    {
        if (deckTargetAnchor != null)
            return WorldToRootLocal(deckTargetAnchor.position, ViewportToLocal(fallbackTargetViewportPosition));

        return ViewportToLocal(fallbackTargetViewportPosition);
    }

    private Vector2 ViewportToLocal(Vector2 viewportPosition)
    {
        Rect rect = presentationRoot.rect;
        float x = Mathf.Lerp(rect.xMin, rect.xMax, viewportPosition.x);
        float y = Mathf.Lerp(rect.yMin, rect.yMax, viewportPosition.y);
        return new Vector2(x, y);
    }

    private Vector2 WorldToRootLocal(Vector3 worldPosition, Vector2 fallback)
    {
        Camera camera = GetCanvasCamera();
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(camera, worldPosition);
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(presentationRoot, screenPoint, camera, out var localPoint)
            ? localPoint
            : fallback;
    }

    private Camera GetCanvasCamera()
    {
        if (_canvas == null)
            _canvas = GetComponentInParent<Canvas>();

        if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return _canvas.worldCamera;
    }

    private void LogPlayerState(string phase)
    {
        if (!debugLogging)
            return;

        Debug.Log($"[RewardCardPresentationAnimator] {phase}: player={GetTransformPath(transform)}, root={GetTransformPath(presentationRoot)}, target={GetTransformPath(deckTargetAnchor)}, prefab={(cardPrefab != null ? cardPrefab.name : "<null>")}, canvas={GetTransformPath(_canvas != null ? _canvas.transform : null)}, rootRect={(presentationRoot != null ? presentationRoot.rect.ToString() : "<null>")}, effectiveAlpha={(presentationRoot != null ? GetEffectiveCanvasGroupAlpha(presentationRoot).ToString("0.###") : "<null>")}");
    }

    private void LogSpawn(CardSO card, CardView view, Vector2 start, Vector2 hold, Vector2 target)
    {
        if (!debugLogging)
            return;

        Debug.Log($"[RewardCardPresentationAnimator] Spawned add-to-deck card: card={GetCardId(card)}, view={GetTransformPath(view.transform)}, root={GetTransformPath(presentationRoot)}, start={start}, hold={hold}, target={target}, viewRect={view.RectTransform.rect}, rootRect={presentationRoot.rect}, effectiveAlpha={GetEffectiveCanvasGroupAlpha(view.transform):0.###}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"[RewardCardPresentationAnimator] {message}");
    }

    private static float GetEffectiveCanvasGroupAlpha(Transform transform)
    {
        float alpha = 1f;
        var current = transform;
        while (current != null)
        {
            if (current.TryGetComponent<CanvasGroup>(out var canvasGroup))
                alpha *= canvasGroup.alpha;

            current = current.parent;
        }

        return alpha;
    }

    private static string GetCardId(CardSO card)
    {
        return card != null ? card.cardId : "<null>";
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        string path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
