using UnityEngine;

[CreateAssetMenu(fileName = "BgmPrompt", menuName = "BlockingKing/Audio/BGM Prompt")]
public sealed class BgmPromptSO : ScriptableObject
{
    [Header("Intent")]
    public string title;
    [TextArea(3, 8)] public string prompt;
    [TextArea(2, 5)] public string negativePrompt;

    [Header("Rhythm")]
    [Min(1f)] public float bpm = 125f;
    [Tooltip("When enabled, DrawSystem beatDuration is half of the musical beat interval. This matches current AllInOne player/enemy round-trip rhythm.")]
    public bool roundTripBeat = true;

    [Header("Generated Result")]
    public AudioClip generatedClip;
    public string generator;
    public string generatedAt;
    [TextArea(2, 4)] public string notes;

    public string ResolvedTitle => !string.IsNullOrWhiteSpace(title)
        ? title
        : generatedClip != null
            ? generatedClip.name
            : name;
}
