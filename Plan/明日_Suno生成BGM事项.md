# 明日 Suno 生成 BGM 事项

优先级：P2

原因：BGM 会提升产品观感，也能展示 `BgmPlaylistSO -> BgmRecordPlayer -> AudioBus -> DrawSystem.ConfigureBeatBpm()` 的节奏联动能力；但它不应压过 P0/P1 的流程反复试玩、美术素材替换、难度曲线修正和 Build 稳定性验证。

## 目标

为当前“霓虹战术沙盘 / 回合推箱子战斗”体验生成 2-4 首可用 BGM，占位但风格统一。

音乐目标：

- instrumental
- no vocals
- loopable / seamless loop
- 120-130 BPM，默认 125 BPM
- 冷静、有压迫、电子感、稳定推进
- 不要史诗管弦，不要强人声，不要过度旋律化

## 推荐流程

1. 创建多个 `BgmPromptSO`
   - 路径：`Create > BlockingKing > Audio > BGM Prompt`
   - 填 `title / prompt / negativePrompt / bpm / roundTripBeat`

2. 用 Suno 生成
   - 优先用文字 prompt 直接生成
   - 如果结果节奏不稳定，再做一个 125 BPM 的 4 小节 click/pulse/drum loop，转成音频上传给 Suno 作为 seed
   - 不考虑直接上传 MIDI；如有 MIDI，先渲染成 wav/mp3 再上传

3. 下载可用结果
   - 导入 Unity
   - 填到对应 `BgmPromptSO.generatedClip`
   - 记录 `generator = Suno`
   - 记录 `generatedAt`
   - 在 `notes` 里写简短评价

4. 加入 `BgmPlaylistSO`
   - 在 playlist 的 track 里引用对应 `BgmPromptSO`
   - 挂到 `RunConfigSO.bgmPlaylist`
   - 进入游戏测试唱片机切歌和 BPM 同步

## Prompt 模板

普通战斗：

```text
Loopable instrumental tactical cyberpunk electro, 125 BPM, no vocals. Neon grid battlefield, turn-based puzzle combat, focused tension, pulsing bass, tight synthetic drums, subtle glitches, minimal synth melody, seamless game loop.
```

更偏解谜：

```text
Loopable instrumental digital puzzle strategy music, 120 BPM, no vocals. Cold holographic board game, careful planning, soft electronic pulses, light glitch percussion, restrained bass, calm tension, seamless loop.
```

更危险：

```text
Loopable instrumental dark tactical electro, 130 BPM, no vocals. Cyber arena under pressure, enemy intent warnings, heavy pulse bass, sharp synthetic hits, glitch accents, tense but controlled, seamless loop.
```

Negative prompt 建议：

```text
vocals, lyrics, orchestral epic trailer, cinematic choir, pop song structure, guitar solo, overly emotional melody, dramatic drop, noisy mix
```

## 验收标准

- 至少 1 首 BGM 能在游戏内正常循环播放。
- 唱片按钮能切歌。
- UI 显示曲名和 BPM。
- 切歌后 `DrawSystem` beat time 跟随 BPM 改变。
- 不影响玩家理解战斗信息，音量默认不要压过 beat 音效。

## 注意

这是 P2。明日如果 P0/P1 未完成，不要在 Suno 上消耗过多时间。
