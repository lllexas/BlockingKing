using System;
using UnityEngine;

[CreateAssetMenu(fileName = "BgmPlaylist", menuName = "BlockingKing/Audio/BGM Playlist")]
public sealed class BgmPlaylistSO : ScriptableObject
{
    [Serializable]
    public sealed class Track
    {
        public BgmPromptSO promptAsset;
        public string title;

        public BgmPromptSO PromptAsset => promptAsset;

        public string ResolvedTitle => promptAsset != null
            ? promptAsset.ResolvedTitle
            : !string.IsNullOrWhiteSpace(title)
                ? title
                : "Untitled";

        public AudioClip ResolvedClip => promptAsset != null && promptAsset.generatedClip != null
            ? promptAsset.generatedClip
            : null;

        public float ResolvedBpm => promptAsset != null ? promptAsset.bpm : 0f;
        public BgmPromptSO.BeatGrouping ResolvedBeatGrouping => promptAsset != null ? promptAsset.beatGrouping : BgmPromptSO.BeatGrouping.QuadBeat;
        public bool ResolvedRoundTripBeat => promptAsset != null && promptAsset.roundTripBeat;
    }

    public Track[] tracks;
    public int defaultTrackIndex;

    public int Count => tracks?.Length ?? 0;

    public Track GetTrack(int index)
    {
        if (tracks == null || tracks.Length == 0)
            return null;

        int wrapped = WrapIndex(index);
        return tracks[wrapped];
    }

    public int WrapIndex(int index)
    {
        int count = Count;
        if (count <= 0)
            return -1;

        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    public int GetDefaultIndex()
    {
        return WrapIndex(defaultTrackIndex);
    }
}
