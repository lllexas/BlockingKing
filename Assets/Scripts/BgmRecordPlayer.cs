using UnityEngine;

public sealed class BgmRecordPlayer : MonoBehaviour
{
    public static BgmRecordPlayer Instance { get; private set; }

    [SerializeField] private BgmPlaylistSO playlist;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool syncBeatTime = true;

    private int _currentTrackIndex = -1;
    private bool _beatSyncPending;

    public BgmPlaylistSO Playlist => playlist;
    public int CurrentTrackIndex => _currentTrackIndex;
    public BgmPlaylistSO.Track CurrentTrack => playlist != null ? playlist.GetTrack(_currentTrackIndex) : null;
    public bool HasTracks => playlist != null && playlist.Count > 0;

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
        if (playOnStart)
            PlayDefault();
    }

    private void Update()
    {
        if (_beatSyncPending)
            ApplyBeatTime(CurrentTrack);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Configure(BgmPlaylistSO newPlaylist, bool startIfIdle = true)
    {
        playlist = newPlaylist;
        if (startIfIdle && _currentTrackIndex < 0)
            PlayDefault();
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

        PlayTrack(_currentTrackIndex < 0 ? playlist.GetDefaultIndex() : _currentTrackIndex + 1);
    }

    public void PreviousTrack()
    {
        if (!HasTracks)
            return;

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

        _currentTrackIndex = wrapped;
        AudioBus.Ensure().PlayMusic(track.ResolvedClip, loop);
        ApplyBeatTime(track);
    }

    private void ApplyBeatTime(BgmPlaylistSO.Track track)
    {
        if (!syncBeatTime || track == null || track.ResolvedBpm <= 0f)
            return;

        var drawSystem = DrawSystem.Instance;
        if (drawSystem == null)
        {
            _beatSyncPending = true;
            return;
        }

        drawSystem.ConfigureBeatBpm(track.ResolvedBpm, track.ResolvedRoundTripBeat);
        _beatSyncPending = false;
    }
}
