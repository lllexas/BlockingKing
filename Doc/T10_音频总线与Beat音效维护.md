# T10 音频总线与 Beat 音效维护

本文记录当前 BlockingKing 的第一版音频方案。目标不是完整音频工程，而是“能响、好排查、以后可扩展”。

## 当前结构

核心文件：

- `Assets/Scripts/AudioBus.cs`
- `Assets/Scripts/Stage/StageBeatAudioSystem.cs`
- `Assets/Scripts/Stage/EventBusSystem.cs`
- `Assets/Scripts/Stage/IntentSystem.cs`

当前只分三类音量：

- `MasterVolume`
- `MusicVolume`
- `SfxVolume`

没有接 `AudioMixer`、Snapshot、复杂 Bus Group。对业务代码暴露的入口只有：

```csharp
AudioBus.Ensure().PlaySfx(clip);
AudioBus.Ensure().PlayMusic(clip);
AudioBus.Ensure().SetMasterVolume(value);
AudioBus.Ensure().SetMusicVolume(value);
AudioBus.Ensure().SetSfxVolume(value);
```

`AudioBus` 内部使用一个 Music `AudioSource` 和一组 SFX `AudioSource` 池。所有 source 都强制 `spatialBlend = 0`，当前音效是 2D 声音，不受相机距离影响。

## Beat 音效原则

Stage 音效第一版不绑定每个敌人的每个 intent。

原因：当前常驻 `AllInOneBatch` 展示模式。如果每个敌人移动或攻击都各播一次音效，敌人数量稍多时声音会堆叠成噪音，也会破坏回合节奏。

当前原则：

- 玩家 beat 播一次音效。
- 敌人 beat 播一次音效。
- 敌人 beat 根据本批次摘要选择一种声音。

## 玩家 Beat

`StageBeatAudioSystem` 监听：

```csharp
StageEventType.BeforeIntentExecute
```

当事件实体是 `EntityType.Player` 时播放玩家 beat。

当前字段：

- `playerMoveBeat`
- `playerPushBoxBeat`
- `playerCardBeat`
- `playerNoopBeat`

`playerPushBoxBeat` 的判定方式：

- 当前玩家 intent 是 `MoveIntent`
- 玩家目标格 `evt.From + moveIntent.Direction` 上有 `EntityType.Box`

这意味着推箱子的音效发生在移动 intent 执行前，能正确读到尚未移动的格子状态。

## 敌人 Beat

`StageBeatAudioSystem` 监听：

```csharp
StageEventType.PresentationBatchBegin
StageEventType.PresentationBeat
```

`IntentSystem` 在构建批次时计算 `EnemyBeatKind`，通过 `StageEvent.EnemyBeatKind` 传给音频系统。

当前敌人字段：

- `enemyAttackBeat`
- `enemySpawnBeat`
- `enemyMoveBeat`
- `enemyEmptyBeat`

优先级：

```text
attack > spawn > move > empty
```

没有 `enemyMixedBeat`。如果一个 AllInOne 敌人批次里同时有移动和攻击，播放 `enemyAttackBeat`。如果没有敌人 intent，但 AllInOne 需要维持节奏空拍，则播放 `enemyEmptyBeat`。

## 场景使用方式

最简单用法：

1. 场景里放一个 `AudioBus`，或让第一次 `AudioBus.Ensure()` 自动创建。
2. `LevelPlayer` 运行时会确保挂上 `StageBeatAudioSystem`。
3. 在 `StageBeatAudioSystem` 上填入对应 `AudioClip`。
4. 确认场景里有且只有一个启用的 `AudioListener`。

URP Camera Stack 场景建议：

- Base/Main Camera 开启 `AudioListener`
- Overlay Camera 不开启 `AudioListener`

这就是当前测试通过的形态。

## 排查步骤

听不到声音时按这个顺序查。

### 1. 查 StageBeatAudioSystem 是否注册

勾选：

```text
StageBeatAudioSystem.logBeatAudio
```

正常应看到：

```text
[StageBeatAudioSystem] Registered stage beat audio handlers.
```

如果看不到，说明 `EventBusSystem` 没准备好，或组件没有启用。当前 `StageBeatAudioSystem.Update()` 会在 `_registeredBus == null` 时持续重试注册。

### 2. 查 beat 是否触发

正常播放前会看到：

```text
[StageBeatAudioSystem] Play beat audio: player Move, clip=...
[StageBeatAudioSystem] Play beat audio: enemy Attack, clip=...
```

如果看到 `clip=null`，说明事件来了，但对应字段没有填 clip。

### 3. 查 AudioBus 是否真的播放

勾选：

```text
AudioBus.logPlayback
```

正常应看到：

```text
[AudioBus] PlaySfx clip=..., sourceVolume=1.00, volumeScale=1.00, pitch=1.00, listeners=1, audioPaused=False
```

如果 `listeners=0`，场景缺 `AudioListener`。

如果 `listeners>1`，场景有多个启用的 `AudioListener`，Unity 会警告并可能表现异常。

如果 `audioPaused=True`，检查 `AudioListener.pause` 是否被其他逻辑设置。

### 4. 快速测试 clip/输出设备

勾选：

```text
StageBeatAudioSystem.testPlayerMoveBeatOnStart
```

如果一进关能听到测试音，说明 `AudioBus / AudioListener / AudioClip / 系统输出` 基本没问题，问题在 Stage 事件链路。

## Unity OnValidate 限制

不要在 `OnValidate()` 里做这些事：

```csharp
new GameObject(...)
AddComponent<T>()
transform.SetParent(...)
```

Unity 会报：

```text
SendMessage cannot be called during Awake, CheckConsistency, or OnValidate
```

当前 `AudioBus.OnValidate()` 只做：

- clamp 音量
- clamp SFX 池大小
- 如果已有 source，则刷新音量

`AudioSource` 子物体只允许在 `Awake()`、`Ensure()`、`PlaySfx()`、`PlayMusic()` 等正常运行阶段创建。

## 后续扩展建议

短期可以继续保持简单：

- 给 `StageBeatAudioSystem` 增加 pitch 随机范围
- 给同一种 beat 支持 clip 列表随机
- 给玩家受伤、箱子受伤、死亡单独接事件音效
- BGM 由 run/stage 状态机控制，不要塞进 intent 系统

中期如果音频复杂度明显上升，再考虑：

- `AudioClipPoolSO`
- 音效事件表
- `AudioMixer`
- 分组 ducking
- BGM 淡入淡出

在此之前，不要为了“音频架构完整”引入大型系统。

## 2026-05-12 BGM 与节拍联动维护记录

本轮把 BGM 从“播放列表里临时挂 clip 和 BPM”收束成以 `BgmPromptSO` 为权威数据源的结构。

### 解决的问题

- `BgmPlaylistSO` 不再重复保存 `clip / bpm`，只负责曲目顺序与选择；实际音频、BPM、节拍类型、回环策略都从 `BgmPromptSO` 读取。
- `RunConfigSO` 增加独立的 `mainMenuBgm`，主菜单音乐不混进关卡播放列表。
- `GameFlowController` 在主菜单启动时禁用唱片机自动播放关卡曲，避免游戏窗口第一次获得焦点前误播关卡 BGM。
- 从主菜单点击开始游戏进入 run 时，会显式切到关卡 BGM。
- `AudioBus.PlayMusic()` 改为顺序换歌：先淡出当前曲，淡出结束后再启动下一首并淡入。主菜单切关卡、关卡内切歌都走同一套逻辑。
- `DrawSystem.ConfigureBeatBpm()` 增加带 `BeatGrouping` 的重载，BGM 切换时由 `BgmRecordPlayer` 统一同步当前 BPM 和节拍分组。
- `BgmPromptSO` 增加 `BeatGrouping`，目前用于区分 `DupleBeat / TripleBeat / QuadBeat / CompoundSix`。
- `IntentSystem` 增加 `AllInTwoBatch`。`TripleBeat` 和 `CompoundSix` 的 BGM 会路由到该模式；`DupleBeat` 和 `QuadBeat` 仍走 `AllInOneBatch`。
- `AllInTwoBatch` 的目标节奏是：玩家占第 1 拍，敌人 Move 占第 2 拍，敌人其他 intent 占第 3 拍。这样 3 拍子和 6 拍子 BGM 不会再被“玩家一拍、敌人一拍”的二拍结构顶爆。

### 促成的效果

- 主菜单曲、正式关卡曲、唱片机切歌有了清晰边界。
- BGM Prompt 资产可以同时承载 Suno 生成提示词、生成结果、BPM、节拍结构和备注，后续调音乐不用在多个 SO 里同步字段。
- UI / 唱片机 / DrawSystem / IntentSystem 都以当前 BGM 为源头同步，减少“曲子换了但节拍没换”的错位风险。
- 支持后续同时准备 2/4、3/4、4/4、6/8 的 BGM：2/4 与 4/4 保持常规敌人合拍展示，3/4 与 6/8 自动拆敌人两拍。

### 当前排查日志

切换 BGM 后，`BgmRecordPlayer` 会输出当前路由结果：

```text
[BgmRecordPlayer] Intent presentation => AllInTwoBatch via BGM '...' (BPM=84, beatGrouping=TripleBeat, roundTripBeat=True)
```

`IntentSystem` 只有在模式实际变化时输出：

```text
[IntentSystem] Enemy intent presentation mode changed: AllInOneBatch -> AllInTwoBatch
```

因此排查时先看 `BgmRecordPlayer` 确认 BGM 被判成哪个 presentation mode，再看 `IntentSystem` 确认模式是否真的发生变化。

### 后续注意

- `AllInTwoBatch` 是新增路径，后续如果敌人 intent 类型继续增加，要确认非 Move intent 是否仍应归入第 3 拍。
- `DrawSystem` 的 BeatTime 现在由 BGM 驱动，但 2/4、3/4、4/4、6/8 对视觉 beat 的精确语义还需要结合实测继续校准。
- 新增 BGM Prompt 资产时必须填写正确的 `beatGrouping`，否则 Intent 展示模式会跟着错。
