using UnityEngine;

[CreateAssetMenu(fileName = "BgmPrompt", menuName = "BlockingKing/Audio/BGM Prompt")]
public sealed class BgmPromptSO : ScriptableObject
{
    public enum BeatGrouping
    {
        SingleBeat = 1,
        DupleBeat = 2,
        TripleBeat = 3,
        QuadBeat = 4,
        CompoundSix = 6
    }

    [Header("Intent")]
    public string title;
    [TextArea(3, 8)] public string promptZh;
    [TextArea(3, 8)] public string promptEn;
    [TextArea(3, 8)] public string prompt;
    [TextArea(2, 5)] public string negativePrompt;

    [Header("Rhythm")]
    [Min(1f)] public float bpm = 125f;
    public BeatGrouping beatGrouping = BeatGrouping.QuadBeat;
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

    public bool UsesAllInTwoPresentation => beatGrouping == BeatGrouping.TripleBeat || beatGrouping == BeatGrouping.CompoundSix;
}

public static class BeatTiming
{
    public const float DefaultBeatDuration = 0.3f;

    public static float GetBeatDuration()
    {
        var drawSystem = DrawSystem.Instance;
        return drawSystem != null ? Mathf.Max(0.03f, drawSystem.BeatDuration) : DefaultBeatDuration;
    }

    public static float GetRoundTripBeatDuration()
    {
        var drawSystem = DrawSystem.Instance;
        return drawSystem != null ? Mathf.Max(0.03f, drawSystem.RoundTripBeatDuration) : DefaultBeatDuration;
    }
}
