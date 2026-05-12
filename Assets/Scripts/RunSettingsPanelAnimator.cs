using SpaceTUI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class RunSettingsPanelAnimator : SpaceUIAnimator
{
    protected override string UIID => RunSettingsUIIds.Panel;

    public static RunSettingsPanelAnimator Instance { get; private set; }

    [Header("Volume")]
    [SerializeField] private Slider masterVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private TextMeshProUGUI masterVolumeValueText;
    [SerializeField] private TextMeshProUGUI sfxVolumeValueText;
    [SerializeField] private TextMeshProUGUI musicVolumeValueText;

    [Header("Buttons")]
    [SerializeField] private Button returnMainMenuButton;

    private bool _isSettingsVisible;
    public bool IsSettingsVisible => _isSettingsVisible;

    protected override void Awake()
    {
        base.Awake();
        Instance = this;
        期望显示面板 += _ => ShowSettings();
        期望隐藏面板 += _ => HideSettings();
        BindSliders();
        BindButtons();
    }

    protected override void OnDestroy()
    {
        UnbindButtons();
        UnbindSliders();
        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }

    public void Toggle()
    {
        if (_isSettingsVisible)
            HideSettings();
        else
            ShowSettings();
    }

    public void ShowSettings()
    {
        if (_isSettingsVisible)
            return;

        _isSettingsVisible = true;
        RefreshFromAudioBus();
        this.FadeInIfHiddenPreserveRotation();
    }

    public void HideSettings()
    {
        if (!_isSettingsVisible)
            return;

        _isSettingsVisible = false;
        this.FadeOutIfVisible();
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid())
            SceneManager.LoadScene(activeScene.buildIndex);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        HideSettings();
        GameFlowController.Instance?.ReturnToMainMenuRound();
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    protected override void CloseAction()
    {
        HideSettings();
    }

    private void BindSliders()
    {
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
    }

    private void UnbindSliders()
    {
        if (masterVolumeSlider != null)
            masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
        if (sfxVolumeSlider != null)
            sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);
        if (musicVolumeSlider != null)
            musicVolumeSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);
    }

    private void BindButtons()
    {
        if (returnMainMenuButton != null)
            returnMainMenuButton.onClick.AddListener(ReturnToMainMenu);
    }

    private void UnbindButtons()
    {
        if (returnMainMenuButton != null)
            returnMainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
    }

    private void RefreshFromAudioBus()
    {
        var bus = AudioBus.Ensure();
        SetSliderWithoutNotify(masterVolumeSlider, bus.MasterVolume);
        SetSliderWithoutNotify(sfxVolumeSlider, bus.SfxVolume);
        SetSliderWithoutNotify(musicVolumeSlider, bus.MusicVolume);
        RefreshValueTexts();
    }

    private void OnMasterVolumeChanged(float value)
    {
        AudioBus.Ensure().SetMasterVolume(value);
        RefreshValueTexts();
    }

    private void OnSfxVolumeChanged(float value)
    {
        AudioBus.Ensure().SetSfxVolume(value);
        RefreshValueTexts();
    }

    private void OnMusicVolumeChanged(float value)
    {
        AudioBus.Ensure().SetMusicVolume(value);
        RefreshValueTexts();
    }

    private void RefreshValueTexts()
    {
        SetPercentText(masterVolumeValueText, masterVolumeSlider != null ? masterVolumeSlider.value : AudioBus.Ensure().MasterVolume);
        SetPercentText(sfxVolumeValueText, sfxVolumeSlider != null ? sfxVolumeSlider.value : AudioBus.Ensure().SfxVolume);
        SetPercentText(musicVolumeValueText, musicVolumeSlider != null ? musicVolumeSlider.value : AudioBus.Ensure().MusicVolume);
    }

    private static void SetSliderWithoutNotify(Slider slider, float value)
    {
        if (slider == null)
            return;

        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(Mathf.Clamp01(value));
    }

    private static void SetPercentText(TextMeshProUGUI text, float value)
    {
        if (text != null)
            text.text = $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%";
    }
}
