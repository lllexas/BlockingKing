using System;
using UnityEngine;

public enum VideoFrameRateMode
{
    FixedFrameRate,
    VSync,
    Unlimited
}

public sealed class VideoSettingsManager : MonoBehaviour
{
    [Serializable]
    public struct FrameRateSettings
    {
        public VideoFrameRateMode mode;
        [Min(1)] public int targetFps;
        [Range(1, 4)] public int vSyncCount;

        public static FrameRateSettings Fixed(int fps)
        {
            return new FrameRateSettings
            {
                mode = VideoFrameRateMode.FixedFrameRate,
                targetFps = Mathf.Max(1, fps),
                vSyncCount = 1
            };
        }

        public static FrameRateSettings VSync(int count)
        {
            return new FrameRateSettings
            {
                mode = VideoFrameRateMode.VSync,
                targetFps = 60,
                vSyncCount = Mathf.Clamp(count, 1, 4)
            };
        }

        public static FrameRateSettings Unlimited()
        {
            return new FrameRateSettings
            {
                mode = VideoFrameRateMode.Unlimited,
                targetFps = 60,
                vSyncCount = 1
            };
        }
    }

    public static VideoSettingsManager Instance { get; private set; }

    [Header("Lifecycle")]
    [SerializeField] private bool applyOnAwake = true;
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("Editor Play Mode")]
    [SerializeField] private bool useEditorOverride = true;
    [SerializeField] private FrameRateSettings editorFrameRate = FrameRateSettings.Fixed(60);

    [Header("Runtime Defaults")]
    [SerializeField] private FrameRateSettings standaloneFrameRate = FrameRateSettings.Fixed(60);
    [SerializeField] private FrameRateSettings mobileFrameRate = FrameRateSettings.Fixed(60);

    private FrameRateSettings _currentFrameRate;

    public VideoFrameRateMode CurrentFrameRateMode => _currentFrameRate.mode;
    public int CurrentTargetFps => _currentFrameRate.targetFps;
    public int CurrentVSyncCount => _currentFrameRate.vSyncCount;

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

        if (applyOnAwake)
            ApplyDefaultFrameRateSettings();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnValidate()
    {
        Normalize(ref editorFrameRate);
        Normalize(ref standaloneFrameRate);
        Normalize(ref mobileFrameRate);

        if (Application.isPlaying && Instance == this)
            ApplyFrameRateSettings(ResolveDefaultFrameRateSettings());
    }

    public static VideoSettingsManager Ensure()
    {
        if (Instance != null)
            return Instance;

        var manager = FindObjectOfType<VideoSettingsManager>();
        if (manager != null)
            return manager;

        var go = new GameObject(nameof(VideoSettingsManager));
        return go.AddComponent<VideoSettingsManager>();
    }

    public void ApplyDefaultFrameRateSettings()
    {
        ApplyFrameRateSettings(ResolveDefaultFrameRateSettings());
    }

    public void ApplyFrameRateSettings(FrameRateSettings settings)
    {
        Normalize(ref settings);
        _currentFrameRate = settings;

        switch (settings.mode)
        {
            case VideoFrameRateMode.FixedFrameRate:
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = settings.targetFps;
                break;

            case VideoFrameRateMode.VSync:
                QualitySettings.vSyncCount = settings.vSyncCount;
                Application.targetFrameRate = -1;
                break;

            case VideoFrameRateMode.Unlimited:
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public void SetFixedFrameRate(int fps)
    {
        ApplyFrameRateSettings(FrameRateSettings.Fixed(fps));
    }

    public void SetVSync(int vSyncCount = 1)
    {
        ApplyFrameRateSettings(FrameRateSettings.VSync(vSyncCount));
    }

    public void SetUnlimitedFrameRate()
    {
        ApplyFrameRateSettings(FrameRateSettings.Unlimited());
    }

    private FrameRateSettings ResolveDefaultFrameRateSettings()
    {
#if UNITY_EDITOR
        if (useEditorOverride)
            return editorFrameRate;
#endif

        if (Application.isMobilePlatform)
            return mobileFrameRate;

        return standaloneFrameRate;
    }

    private static void Normalize(ref FrameRateSettings settings)
    {
        settings.targetFps = Mathf.Max(1, settings.targetFps);
        settings.vSyncCount = Mathf.Clamp(settings.vSyncCount, 1, 4);
    }

#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureEditorPlayModeLimiter()
    {
        Ensure();
    }
#endif
}
