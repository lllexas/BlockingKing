using Sirenix.OdinInspector;
using UnityEngine;

[CreateAssetMenu(fileName = "RunStartSettings", menuName = "BlockingKing/Run Start Settings")]
public class RunStartSettings : ScriptableObject
{
    [Title("Presentation")]
    public string displayName;

    [TextArea(2, 5)]
    public string description;

    public Sprite icon;

    [Title("Starting Deck")]
    [AssetsOnly]
    public StartingDeckSO startingDeck;

    [Title("Hand")]
    [Min(0)]
    public int targetHandCount = 5;

    [Min(0)]
    public int maxHandCount = 10;

    public bool autoRefill = true;
}
