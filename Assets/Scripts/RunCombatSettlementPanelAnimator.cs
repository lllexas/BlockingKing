using System.Collections;
using SpaceTUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RunCombatSettlementPanelAnimator : SpaceUIAnimator
{
    protected override string UIID => RunRoundUIIds.CombatSettlement;

    [Header("Reveal Timing")]
    [SerializeField, Min(0f)] private float titleBeatDelay = 0.5f;
    [SerializeField, Min(0f)] private float levelBeatDelay = 0.5f;
    [SerializeField, Min(0f)] private float boxBeatDelay = 0.5f;
    [SerializeField, Min(0f)] private float rewardHeaderBeatDelay = 0.35f;
    [SerializeField, Min(0f)] private float rewardLineBeatDelay = 0.5f;
    [SerializeField, Min(0f)] private float totalBeatDelay = 0.5f;
    [SerializeField, Min(0f)] private float footerBeatDelay = 0.5f;

    [Header("Settlement UI")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI boxText;
    [SerializeField] private TextMeshProUGUI rewardLinesText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI hpText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Button continueButton;

    private RunRoundController _controller;
    private RunCombatSettlement _settlement;
    private Coroutine _revealRoutine;

    protected override void Awake()
    {
        base.Awake();
        期望显示面板 += OnShowPanel;
        期望隐藏面板 += _ => this.FadeOutIfVisible();
        BindButtons();
    }

    protected override void OnDestroy()
    {
        StopReveal();
        UnbindButtons();
        base.OnDestroy();
    }

    protected override void CloseAction()
    {
        Continue();
    }

    private void OnShowPanel(object data)
    {
        if (data is not RunCombatSettlementUIRequest request)
            return;

        _controller = request.Controller;
        _settlement = request.Settlement;
        StartReveal(_settlement);
        this.FadeInIfHidden();
    }

    private void StartReveal(RunCombatSettlement settlement)
    {
        StopReveal();
        ClearTexts();
        SetContinueVisible(false);
        _revealRoutine = StartCoroutine(RevealRoutine(settlement));
    }

    private IEnumerator RevealRoutine(RunCombatSettlement settlement)
    {
        SetText(titleText, "战斗结算");
        yield return WaitBeats(titleBeatDelay);

        if (settlement == null)
        {
            SetContinueVisible(true);
            _revealRoutine = null;
            yield break;
        }

        string modeName = settlement.Mode == RunMainStageMode.Escort ? "Escort" : "Classic";
        string levelName = string.IsNullOrWhiteSpace(settlement.LevelName) ? "未知关卡" : settlement.LevelName;
        SetText(levelText, $"{modeName} · {levelName}");
        yield return WaitBeats(levelBeatDelay);

        SetText(boxText, $"成功归位箱子 x{settlement.SuccessfulBoxCount}");
        yield return WaitBeats(boxBeatDelay);

        var builder = new System.Text.StringBuilder();
        if (settlement.RewardLines.Count == 0)
        {
            SetText(rewardLinesText, "无金币奖励");
            yield return WaitBeats(rewardLineBeatDelay);
        }
        else
        {
            for (int i = 0; i < settlement.RewardLines.Count; i++)
            {
                var line = settlement.RewardLines[i];
                if (line == null)
                    continue;

                if (builder.Length > 0)
                    builder.AppendLine();

                builder.Append(BuildRewardLine(line));
                SetText(rewardLinesText, builder.ToString());
                yield return WaitBeats(line.IsHeader ? rewardHeaderBeatDelay : rewardLineBeatDelay);
            }
        }

        SetText(goldText, $"总计获得 {FormatDelta(settlement.GoldDelta)} 金币  |  当前金币 {settlement.GoldAfter}");
        yield return WaitBeats(totalBeatDelay);

        SetText(hpText, settlement.MaxHp > 0 ? $"HP {settlement.Hp}/{settlement.MaxHp}" : "HP -");
        SetText(messageText, settlement.Message);
        yield return WaitBeats(footerBeatDelay);

        SetContinueVisible(true);
        _revealRoutine = null;
    }

    private void Continue()
    {
        if (_revealRoutine != null)
        {
            StopReveal();
            RevealAll(_settlement);
            SetContinueVisible(true);
            return;
        }

        this.FadeOutIfVisible();
        _controller?.ConfirmCombatSettlement();
    }

    private void RevealAll(RunCombatSettlement settlement)
    {
        SetText(titleText, "战斗结算");
        if (settlement == null)
            return;

        string modeName = settlement.Mode == RunMainStageMode.Escort ? "Escort" : "Classic";
        string levelName = string.IsNullOrWhiteSpace(settlement.LevelName) ? "未知关卡" : settlement.LevelName;
        SetText(levelText, $"{modeName} · {levelName}");
        SetText(boxText, $"成功归位箱子 x{settlement.SuccessfulBoxCount}");
        SetText(rewardLinesText, BuildRewardLines(settlement));
        SetText(goldText, $"总计获得 {FormatDelta(settlement.GoldDelta)} 金币  |  当前金币 {settlement.GoldAfter}");
        SetText(hpText, settlement.MaxHp > 0 ? $"HP {settlement.Hp}/{settlement.MaxHp}" : "HP -");
        SetText(messageText, settlement.Message);
    }

    private void StopReveal()
    {
        if (_revealRoutine == null)
            return;

        StopCoroutine(_revealRoutine);
        _revealRoutine = null;
    }

    private IEnumerator WaitBeats(float beats)
    {
        float beatDuration = Mathf.Max(0.05f, BeatTiming.GetBeatDuration());
        float waitSeconds = beatDuration * Mathf.Max(0f, beats);
        if (waitSeconds <= 0f)
            yield break;

        yield return new WaitForSeconds(waitSeconds);
    }

    private void ClearTexts()
    {
        SetText(titleText, string.Empty);
        SetText(levelText, string.Empty);
        SetText(boxText, string.Empty);
        SetText(rewardLinesText, string.Empty);
        SetText(goldText, string.Empty);
        SetText(hpText, string.Empty);
        SetText(messageText, string.Empty);
    }

    private void SetContinueVisible(bool visible)
    {
        if (continueButton == null)
            return;

        continueButton.gameObject.SetActive(visible);
        continueButton.interactable = visible;
    }

    private void BindButtons()
    {
        if (continueButton != null)
            continueButton.onClick.AddListener(Continue);
    }

    private void UnbindButtons()
    {
        if (continueButton != null)
            continueButton.onClick.RemoveListener(Continue);
    }

    private static string FormatDelta(int value)
    {
        return value >= 0 ? $"+{value}" : value.ToString();
    }

    private static string BuildRewardLines(RunCombatSettlement settlement)
    {
        if (settlement == null || settlement.RewardLines.Count == 0)
            return "无金币奖励";

        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < settlement.RewardLines.Count; i++)
        {
            var line = settlement.RewardLines[i];
            if (line == null)
                continue;

            if (builder.Length > 0)
                builder.AppendLine();

            builder.Append(BuildRewardLine(line));
        }

        return builder.ToString();
    }

    private static string BuildRewardLine(RunCombatSettlementRewardLine line)
    {
        if (line.IsHeader)
            return $"{line.Label}:";

        return $"{line.Label}  ->  {FormatDelta(line.Gold)} 金币";
    }

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }
}
