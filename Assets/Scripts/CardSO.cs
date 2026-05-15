using UnityEngine;
using NekoGraph;
using Newtonsoft.Json;

[CreateAssetMenu(fileName = "Card", menuName = "BlockingKing/Cards/Card")]
[VFSContentKind(VFSContentKind.UnityObject)]
[JsonObject(MemberSerialization.OptIn)]
public class CardSO : ScriptableObject
{
    [Header("Identity")]
    [JsonProperty]
    public string instanceId;

    [JsonProperty]
    public string cardId;

    [JsonProperty]
    public string displayName;

    [JsonProperty]
    [TextArea(2, 5)]
    public string description;

    [Header("Rules")]
    [JsonProperty]
    public int cost = 1;

    [JsonProperty]
    public CardReleaseRule releaseRule = CardReleaseRule.EightWayLine;

    [Header("Presentation")]
    [JsonIgnore]
    public Sprite icon;
}
