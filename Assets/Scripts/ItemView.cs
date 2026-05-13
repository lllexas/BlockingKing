using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public sealed class ItemView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Sprite fallbackIcon;

    private System.Action<ItemView> _clickHandler;

    public ItemSO Item { get; private set; }
    public string FallbackName { get; private set; }

    public RectTransform RectTransform => transform as RectTransform;

    public void Bind(ItemSO item, string fallbackName, System.Action<ItemView> onClicked)
    {
        Item = item;
        FallbackName = fallbackName;
        _clickHandler = onClicked;

        string displayName = item != null ? item.ResolvedDisplayName : fallbackName;
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = "Item";

        if (nameText != null)
            nameText.text = displayName;

        if (iconImage != null)
        {
            iconImage.sprite = item != null && item.icon != null ? item.icon : fallbackIcon;
            iconImage.enabled = iconImage.sprite != null;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        _clickHandler?.Invoke(this);
    }
}
