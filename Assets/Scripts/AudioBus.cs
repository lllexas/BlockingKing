using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal runtime audio bus. Keeps the game code away from AudioSource details.
/// </summary>
public class AudioBus : MonoBehaviour
{
    public static AudioBus Instance { get; private set; }

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;

    [Header("Runtime")]
    [SerializeField, Range(1, 32)] private int sfxSourcePoolSize = 8;
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool logPlayback;

    private AudioSource _musicSource;
    private readonly List<AudioSource> _sfxSources = new();
    private int _nextSfxSourceIndex;

    public float MasterVolume => masterVolume;
    public float MusicVolume => musicVolume;
    public float SfxVolume => sfxVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (dontDestroyOnLoad)
            DontDestroyOnLoad(gameObject);

        EnsureSources();
        ApplyVolumes();
    }

    private void OnValidate()
    {
        masterVolume = Mathf.Clamp01(masterVolume);
        musicVolume = Mathf.Clamp01(musicVolume);
        sfxVolume = Mathf.Clamp01(sfxVolume);
        sfxSourcePoolSize = Mathf.Max(1, sfxSourcePoolSize);

        if (Application.isPlaying && _musicSource != null)
            ApplyVolumes();
    }

    public static AudioBus Ensure()
    {
        if (Instance != null)
            return Instance;

        var go = new GameObject(nameof(AudioBus));
        return go.AddComponent<AudioBus>();
    }

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        ApplyVolumes();
    }

    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        ApplyVolumes();
    }

    public void SetSfxVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        ApplyVolumes();
    }

    public void PlayMusic(AudioClip clip, bool loop = true)
    {
        EnsureSources();
        if (_musicSource == null)
            return;

        if (clip == null)
        {
            StopMusic();
            return;
        }

        if (_musicSource.clip == clip && _musicSource.isPlaying)
            return;

        _musicSource.clip = clip;
        _musicSource.loop = loop;
        _musicSource.volume = GetMusicOutputVolume();
        _musicSource.Play();
    }

    public void StopMusic()
    {
        if (_musicSource == null)
            return;

        _musicSource.Stop();
        _musicSource.clip = null;
    }

    public void PlaySfx(AudioClip clip, float volumeScale = 1f, float pitch = 1f)
    {
        if (clip == null)
        {
            if (logPlayback)
                Debug.LogWarning("[AudioBus] PlaySfx ignored: clip is null.");
            return;
        }

        EnsureSources();
        if (_sfxSources.Count == 0)
        {
            if (logPlayback)
                Debug.LogWarning("[AudioBus] PlaySfx ignored: no sfx sources.");
            return;
        }

        var source = _sfxSources[_nextSfxSourceIndex];
        _nextSfxSourceIndex = (_nextSfxSourceIndex + 1) % _sfxSources.Count;

        source.pitch = Mathf.Max(0.01f, pitch);
        source.volume = GetSfxOutputVolume();
        source.PlayOneShot(clip, Mathf.Clamp01(volumeScale));

        if (logPlayback)
        {
            int listenerCount = FindObjectsOfType<AudioListener>().Length;
            Debug.Log($"[AudioBus] PlaySfx clip={clip.name}, sourceVolume={source.volume:0.00}, volumeScale={volumeScale:0.00}, pitch={source.pitch:0.00}, listeners={listenerCount}, audioPaused={AudioListener.pause}");
        }
    }

    private void EnsureSources()
    {
        if (_musicSource == null)
        {
            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(transform, false);
            _musicSource = musicGo.AddComponent<AudioSource>();
            _musicSource.playOnAwake = false;
            _musicSource.loop = true;
            _musicSource.spatialBlend = 0f;
        }

        while (_sfxSources.Count < sfxSourcePoolSize)
        {
            var sfxGo = new GameObject($"Sfx_{_sfxSources.Count:00}");
            sfxGo.transform.SetParent(transform, false);
            var source = sfxGo.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            _sfxSources.Add(source);
        }
    }

    private void ApplyVolumes()
    {
        if (_musicSource != null)
            _musicSource.volume = GetMusicOutputVolume();

        for (int i = 0; i < _sfxSources.Count; i++)
        {
            if (_sfxSources[i] != null)
                _sfxSources[i].volume = GetSfxOutputVolume();
        }
    }

    private float GetMusicOutputVolume()
    {
        return masterVolume * musicVolume;
    }

    private float GetSfxOutputVolume()
    {
        return masterVolume * sfxVolume;
    }
}
