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
    [SerializeField, Min(0f)] private float musicFadeDuration = 0.75f;
    [SerializeField] private bool dontDestroyOnLoad = true;
    [SerializeField] private bool logPlayback;

    private AudioSource _musicSource;
    private AudioSource _musicSourceB;
    private AudioSource _activeMusicSource;
    private Coroutine _musicFadeRoutine;
    private float _activeMusicVolumeScale = 1f;
    private float _activeMusicVolumeOffsetScale = 1f;
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

    public void SetActiveMusicVolumeOffset(float decibels)
    {
        _activeMusicVolumeOffsetScale = DecibelsToLinear(decibels);
        ApplyVolumes();
    }

    public void PlayMusic(AudioClip clip, bool loop = true)
    {
        PlayMusic(clip, loop, musicFadeDuration);
    }

    public void PlayMusicWithVolumeOffset(AudioClip clip, bool loop, float volumeOffsetDb)
    {
        PlayMusic(clip, loop, musicFadeDuration, volumeOffsetDb);
    }

    public void PlayMusic(AudioClip clip, bool loop, float fadeDuration)
    {
        PlayMusic(clip, loop, fadeDuration, 0f);
    }

    public void PlayMusic(AudioClip clip, bool loop, float fadeDuration, float volumeOffsetDb)
    {
        EnsureSources();
        if (_activeMusicSource == null)
            return;

        if (clip == null)
        {
            StopMusic(fadeDuration);
            return;
        }

        float volumeOffsetScale = DecibelsToLinear(volumeOffsetDb);
        if (_activeMusicSource.clip == clip && _activeMusicSource.isPlaying)
        {
            _activeMusicVolumeOffsetScale = volumeOffsetScale;
            ApplyVolumes();
            return;
        }

        ReplaceMusic(clip, loop, fadeDuration, volumeOffsetScale);
    }

    public void StopMusic()
    {
        StopMusic(musicFadeDuration);
    }

    public void StopMusic(float fadeDuration)
    {
        if (_activeMusicSource == null)
            return;

        if (_musicFadeRoutine != null)
            StopCoroutine(_musicFadeRoutine);

        if (fadeDuration <= 0f || !_activeMusicSource.isPlaying)
        {
            StopSource(_activeMusicSource);
            _activeMusicVolumeScale = 0f;
            ApplyVolumes();
            return;
        }

        _musicFadeRoutine = StartCoroutine(FadeOutMusic(fadeDuration));
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

        if (_musicSourceB == null)
        {
            var musicGo = new GameObject("Music_B");
            musicGo.transform.SetParent(transform, false);
            _musicSourceB = musicGo.AddComponent<AudioSource>();
            _musicSourceB.playOnAwake = false;
            _musicSourceB.loop = true;
            _musicSourceB.spatialBlend = 0f;
        }

        _activeMusicSource ??= _musicSource;

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
            _musicSource.volume = GetMusicOutputVolume() * GetMusicVolumeScale(_musicSource);

        if (_musicSourceB != null)
            _musicSourceB.volume = GetMusicOutputVolume() * GetMusicVolumeScale(_musicSourceB);

        for (int i = 0; i < _sfxSources.Count; i++)
        {
            if (_sfxSources[i] != null)
                _sfxSources[i].volume = GetSfxOutputVolume();
        }
    }

    private float GetMusicOutputVolume()
    {
        return masterVolume * musicVolume * _activeMusicVolumeOffsetScale;
    }

    private float GetSfxOutputVolume()
    {
        return masterVolume * sfxVolume;
    }

    private void ReplaceMusic(AudioClip clip, bool loop, float fadeDuration, float volumeOffsetScale)
    {
        if (_musicFadeRoutine != null)
            StopCoroutine(_musicFadeRoutine);

        var source = _activeMusicSource ?? _musicSource;
        if (source == null)
            return;

        if (fadeDuration <= 0f || !source.isPlaying)
        {
            StopSource(source);
            source.clip = clip;
            source.loop = loop;
            _activeMusicVolumeOffsetScale = volumeOffsetScale;
            source.volume = GetMusicOutputVolume();
            source.Play();
            _activeMusicSource = source;
            _activeMusicVolumeScale = 1f;
            ApplyVolumes();
            return;
        }

        _musicFadeRoutine = StartCoroutine(ReplaceMusicRoutine(source, clip, loop, fadeDuration, volumeOffsetScale));
    }

    private System.Collections.IEnumerator ReplaceMusicRoutine(AudioSource source, AudioClip clip, bool loop, float duration, float volumeOffsetScale)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetMusicVolumeScale(source, 1f - t);
            ApplyVolumes();
            yield return null;
        }

        StopSource(source);
        SetMusicVolumeScale(source, 0f);
        ApplyVolumes();

        source.clip = clip;
        source.loop = loop;
        _activeMusicVolumeOffsetScale = volumeOffsetScale;
        source.volume = 0f;
        source.Play();
        _activeMusicSource = source;

        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetMusicVolumeScale(source, t);
            ApplyVolumes();
            yield return null;
        }

        SetMusicVolumeScale(source, 1f);
        ApplyVolumes();
        _musicFadeRoutine = null;
    }

    private System.Collections.IEnumerator FadeOutMusic(float duration)
    {
        var source = _activeMusicSource;
        float startScale = GetMusicVolumeScale(source);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            SetMusicVolumeScale(source, Mathf.Lerp(startScale, 0f, t));
            ApplyVolumes();
            yield return null;
        }

        StopSource(source);
        SetMusicVolumeScale(source, 0f);
        ApplyVolumes();
        _musicFadeRoutine = null;
    }

    private float GetMusicVolumeScale(AudioSource source)
    {
        if (source == _activeMusicSource)
            return _activeMusicVolumeScale;

        return 0f;
    }

    private void SetMusicVolumeScale(AudioSource source, float value)
    {
        value = Mathf.Clamp01(value);
        if (source == _activeMusicSource)
            _activeMusicVolumeScale = value;
    }

    private static void StopSource(AudioSource source)
    {
        if (source == null)
            return;

        source.Stop();
        source.clip = null;
    }

    private static float DecibelsToLinear(float decibels)
    {
        return Mathf.Pow(10f, decibels / 20f);
    }
}
