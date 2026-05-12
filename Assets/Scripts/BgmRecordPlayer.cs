using UnityEngine;

public sealed class BgmRecordPlayer : MonoBehaviour
{
    public static BgmRecordPlayer Instance { get; private set; }

    [SerializeField] private BgmPlaylistSO playlist;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool syncBeatTime = true;

    private int _currentTrackIndex = -1;
    private BgmPromptSO _currentPrompt;
    private bool _hasPlaybackStarted;
    private bool _beatSyncPending;
    private float _lastAppliedBpm = -1f;
    private bool _lastAppliedRoundTripBeat;
    private BgmPromptSO.BeatGrouping _lastAppliedBeatGrouping = BgmPromptSO.BeatGrouping.QuadBeat;

    public BgmPlaylistSO Playlist => playlist;
    public int CurrentTrackIndex => _currentTrackIndex;
    public BgmPlaylistSO.Track CurrentTrack => playlist != null ? playlist.GetTrack(_currentTrackIndex) : null;
    public BgmPromptSO CurrentPrompt => _currentPrompt;
    public bool HasTracks => playlist != null && playlist.Count > 0;
    public bool PlayOnStart
    {
        get => playOnStart;
        set => playOnStart = value;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (playOnStart && !_hasPlaybackStarted)
            PlayDefault();
    }

    private void Update()
    {
        if (_beatSyncPending)
        {
            var track = CurrentTrack;
            if (track != null)
                ApplyBeatTime(track.ResolvedBpm, track.ResolvedBeatGrouping, track.ResolvedRoundTripBeat);
            else if (_currentPrompt != null)
                ApplyBeatTime(_currentPrompt.bpm, _currentPrompt.beatGrouping, _currentPrompt.roundTripBeat);
        }

        SyncBeatTimeContinuously();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Configure(BgmPlaylistSO newPlaylist, bool startIfIdle = true)
    {
        playlist = newPlaylist;
        PreloadPlaylistAudio(playlist);
        if (startIfIdle && _currentTrackIndex < 0)
            PlayDefault();
    }

    public void PlayPrompt(BgmPromptSO prompt, bool loop = true)
    {
        if (prompt == null || prompt.generatedClip == null)
            return;

        PreloadAudio(prompt.generatedClip);
        _currentTrackIndex = -1;
        _currentPrompt = prompt;
        _hasPlaybackStarted = true;
        AudioBus.Ensure().PlayMusic(prompt.generatedClip, loop);
        ApplyBeatConfiguration(prompt);
    }

    public void PlayDefault()
    {
        if (!HasTracks)
            return;

        PlayTrack(playlist.GetDefaultIndex());
    }

    public void NextTrack()
    {
        if (!HasTracks)
            return;

        _currentPrompt = null;
        PlayTrack(_currentTrackIndex < 0 ? playlist.GetDefaultIndex() : _currentTrackIndex + 1);
    }

    public void PreviousTrack()
    {
        if (!HasTracks)
            return;

        _currentPrompt = null;
        PlayTrack(_currentTrackIndex < 0 ? playlist.GetDefaultIndex() : _currentTrackIndex - 1);
    }

    public void PlayTrack(int index)
    {
        if (!HasTracks)
            return;

        int wrapped = playlist.WrapIndex(index);
        var track = playlist.GetTrack(wrapped);
        if (track == null || track.ResolvedClip == null)
            return;

        PreloadAudio(track.ResolvedClip);
        _currentPrompt = null;
        _hasPlaybackStarted = true;
        _currentTrackIndex = wrapped;
        AudioBus.Ensure().PlayMusic(track.ResolvedClip, loop);
        ApplyBeatConfiguration(track.PromptAsset);
    }

    private void ApplyBeatConfiguration(BgmPromptSO prompt)
    {
        if (prompt == null)
            return;

        ApplyBeatTime(prompt.bpm, prompt.beatGrouping, prompt.roundTripBeat);
        ConfigureIntentPresentation(prompt);
    }

    private void ApplyBeatTime(float bpm, BgmPromptSO.BeatGrouping beatGrouping, bool roundTripBeat)
    {
        if (!syncBeatTime || bpm <= 0f)
            return;

        var drawSystem = DrawSystem.Instance;
        if (drawSystem == null)
        {
            _beatSyncPending = true;
            return;
        }

        drawSystem.ConfigureBeatBpm(bpm, beatGrouping, roundTripBeat);
        _beatSyncPending = false;
        _lastAppliedBpm = bpm;
        _lastAppliedRoundTripBeat = roundTripBeat;
        _lastAppliedBeatGrouping = beatGrouping;
    }

    private static void ConfigureIntentPresentation(BgmPromptSO prompt)
    {
        var intentSystem = IntentSystem.Instance;
        if (intentSystem == null || prompt == null)
            return;

        var mode = prompt.UsesAllInTwoPresentation
            ? EnemyIntentPresentationMode.AllInTwoBatch
            : EnemyIntentPresentationMode.AllInOneBatch;

        intentSystem.ConfigureEnemyIntentPresentation(mode);
        Debug.Log($"[BgmRecordPlayer] Intent presentation => {mode} via BGM '{prompt.ResolvedTitle}' (BPM={prompt.bpm:0.#}, beatGrouping={prompt.beatGrouping}, roundTripBeat={prompt.roundTripBeat})");
    }

    private void SyncBeatTimeContinuously()
    {
        var drawSystem = DrawSystem.Instance;
        if (drawSystem == null)
            return;

        if (_currentPrompt != null)
        {
            if (!Mathf.Approximately(_lastAppliedBpm, _currentPrompt.bpm) ||
                _lastAppliedRoundTripBeat != _currentPrompt.roundTripBeat ||
                _lastAppliedBeatGrouping != _currentPrompt.beatGrouping)
            {
                ApplyBeatConfiguration(_currentPrompt);
            }

            return;
        }

        var track = CurrentTrack;
        if (track == null)
            return;

        if (!Mathf.Approximately(_lastAppliedBpm, track.ResolvedBpm) ||
            _lastAppliedRoundTripBeat != track.ResolvedRoundTripBeat ||
            _lastAppliedBeatGrouping != track.ResolvedBeatGrouping)
        {
            ApplyBeatConfiguration(track.PromptAsset);
        }
    }

    private static void PreloadPlaylistAudio(BgmPlaylistSO targetPlaylist)
    {
        if (targetPlaylist == null || targetPlaylist.tracks == null)
            return;

        for (int i = 0; i < targetPlaylist.tracks.Length; i++)
            PreloadAudio(targetPlaylist.tracks[i]?.ResolvedClip);
    }

    private static void PreloadAudio(AudioClip clip)
    {
        if (clip == null || clip.loadState == AudioDataLoadState.Loaded || clip.loadState == AudioDataLoadState.Loading)
            return;

        clip.LoadAudioData();
    }
}
