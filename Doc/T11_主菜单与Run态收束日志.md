# T11 主菜单与 Run 态收束日志

## 本轮解决了什么

1. 主菜单和正式 Run 的生命周期拆清了。
   - 冷启动主菜单不再走 FadeIn。
   - 返回主菜单仍然保留淡入。
   - 主菜单按钮、设置面板、唱片机按钮都按各自入口恢复，不再互相污染。

2. 唱片机按钮的运行语义修正了。
   - 它不再被当成 round selection 的附属物。
   - 进入 Run 后会重新显现。
   - 全局清场仍然保留，不靠子类绕规则。

3. 主菜单背景有了独立的屏幕空间底板。
   - 用屏幕坐标 shader 复现了菱形网络基底。
   - 它可以直接挂在全屏 Image 上。
   - 不再依赖世界坐标，也不要求物体位置固定。

4. 场景后处理补齐了。
   - 参考了另一项目的 Volume 配置。
   - 加了全局 Volume profile。
   - 打开了主相机的 post processing。

5. 旧的地图外缘 runtime 底板链路删掉了。
   - `OutsideDiamond` 不再由 `LevelPlayer` 动态生成。
   - 需要时改为场景里手工放 quad，再挂材质。

## 带来的体验

1. 冷启动更干净。
   - 主菜单不会闪出底层背景。
   - 进入游戏的第一眼更稳。

2. 菜单层级更清楚。
   - 主菜单、设置、Run 内 UI 不再靠一套模糊规则互相遮挡。
   - 返回主菜单的动效和首次进入的动效分开了。

3. 背景观感更完整。
   - 菱形网络能作为主菜单底板直接使用。
   - 后处理把整体画面从“平面 UI”拉回到更像产品页的质感。

4. 维护成本更低。
   - 运行时自动拼地板那套链路去掉后，场景职责更明确。
   - 哪些东西是场景资产，哪些东西是运行态生成，不再混在一起。

## 相关文件

- [Assets/Scripts/GameFlowController.cs](file:///G:/ProjectOfGame/BlockingKing/Assets/Scripts/GameFlowController.cs)
- [Assets/Scripts/MainMenuAnimatorBase.cs](file:///G:/ProjectOfGame/BlockingKing/Assets/Scripts/MainMenuAnimatorBase.cs)
- [Assets/Scripts/BgmRecordAnimator.cs](file:///G:/ProjectOfGame/BlockingKing/Assets/Scripts/BgmRecordAnimator.cs)
- [Assets/Scripts/RunRoundController.cs](file:///G:/ProjectOfGame/BlockingKing/Assets/Scripts/RunRoundController.cs)
- [Assets/Scripts/LevelPlayer.cs](file:///G:/ProjectOfGame/BlockingKing/Assets/Scripts/LevelPlayer.cs)
- [Assets/Shaders/MainMenuScreenDiamond.shader](file:///G:/ProjectOfGame/BlockingKing/Assets/Shaders/MainMenuScreenDiamond.shader)
- [Assets/Settings/BigMapVolumeProfile.asset](file:///G:/ProjectOfGame/BlockingKing/Assets/Settings/BigMapVolumeProfile.asset)
- [Assets/Scenes/StageScene.unity](file:///G:/ProjectOfGame/BlockingKing/Assets/Scenes/StageScene.unity)
