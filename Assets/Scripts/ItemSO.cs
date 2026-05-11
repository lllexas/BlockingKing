using NekoGraph;
using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "BlockingKing/Item")]
[VFSContentKind(VFSContentKind.UnityObject)]
public sealed class ItemSO : ScriptableObject
{
    public string itemId;
    public string itemType;
    public string displayName;

    [TextArea(2, 5)]
    public string description;

    public string ResolvedItemId => string.IsNullOrWhiteSpace(itemId) ? name : itemId;
    public string ResolvedDisplayName => string.IsNullOrWhiteSpace(displayName) ? ResolvedItemId : displayName;
}
