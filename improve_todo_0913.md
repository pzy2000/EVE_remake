# STARFALL ODYSSEY 改进计划（第二轮 · 2026-09-13）

> 来源：M1-M10 落地后的第二轮全量审查（Simulation / UI / Presentation / App / Persistence 三路并行逐行审查，关键行号均已亲自复核）。
> 范围：Unity Android 版（主力交付物）。
> 实施方式：按里程碑推进，每个里程碑完成后 git commit 一次，附 EditMode 单元测试 + 编译验证；涉及渲染/触屏的里程碑追加设备验证。

---

## M1 — 存档完整性与经济公平（P0 + P1，模拟核心）

**1. [P0] 自动存档/切后台存档绕过 SyncPlayerShip + PersistSystemWorld → 太空中退出=满血复原 + 矿石复制**
- `App/AppRoot.cs:159-163` `AutosaveNow()` 直接 `Save()` 序列化 `state.Player`；舰船血量只在 dock/jump/手动存档/死亡时回写（`GameSession.cs:1817`），小行星余量只在 `PersistSystemWorld()`（`:1053`）回写。
- 复现 A：出站→被打残→切后台（Android 触发 `OnApplicationPause`）→ Continue → 船满血原地复活。
- 复现 B：采矿装满→自动存档→强退→重载：货舱矿石在、矿带余量回滚到上次进站 → 无限矿石。
- 修复：`GameSession` 增加 `PrepareForSave()`（内部调 `SyncPlayerShip()+PersistSystemWorld()`，两方法已有空守卫），`AutosaveNow()` 先调它再存。

**2. [P1] 战列巡洋舰(BC)赏金=战列舰(BS) 350k，L4 任务经济断层 ×4~7**
- `GameSession.cs:685` 赏金三元链没有 `Battlecruiser` 分支，BC 落入 else 桶拿 350k。L4 安保任务 6×BC×安等加成=210万~315万，L3 仅 ~54 万。
- 修复：BC 独立档 **180k**（介于巡洋 90k 与战列 350k 之间），L4 总赏金收敛到 ×2.5~3。

**3. [P1] 海盗"低血逃跑"秒消失——交通流海盗赏金实际拿不到**
- `GameSession.cs:878-887`：`npc.AiTime` 是出生至今累计时长（`:820` 累加、从不重置），船体 <30% 时 `AiTime>4d` 几乎恒真 → 下一帧直接消失。设计意图"逃跑 4 秒后跃迁"从未生效，玩家打掉 70% 血分文无收。
- 修复：改用 `State.SimulationTime` 记录开始逃跑时刻（`FleeSince`），逃跑满 4s 才 despawn；修正注释。

**测试**：EditMode 单测覆盖三件：autosave 前血量/矿量已同步、BC 赏金=180k 基准、海盗 30% 血后 4s 内不消失/超时消失。

## M2 — 设置、音频与渲染缓存（P1，表现层）

**4. [P1] SFX 音量滑条完全失效——值变更回调从未注册**
- `UI/StarfallSettingsPanel.cs:97-99` 只注册了 music/mute/uiScale 三个控件，`OnSfxVolumeChanged`（`:202-207`）是死代码：拖 SFX 滑条无任何效果、百分比不更新。`Dispose()`（`:123-128`）也漏注销其 PointerUp。
- 修复：补 `sfxVolumeSlider.RegisterValueChangedCallback(OnSfxVolumeChanged)` + Dispose 对称注销。

**5. [P1] SFX 路径每 tick 全量 `PlayerPrefs.Save()`（fsync）——音乐路径修了，音效没修**
- `Presentation/StarfallSfx.cs:62-67` `SetVolume` 无条件落盘；拖动期间每次步进都 fsync。音乐路径已用 `persist:false` 暂存模式（`MusicDirector.cs:113-122`）。
- 修复：`SetVolume(value, persist=true)`，滑条拖动期间传 false，抬手由 `OnSliderReleased` 统一 flush。

**6. [P1] 行星贴图淘汰与材质缓存不同步——重访旧星系行星变纯色**
- `Presentation/ProceduralSpaceMaterials.cs:78` 超过 32 张销毁非天空贴图，但 `GetPlanetMaterial`（`:231-246`）缓存命中直接返回，材质 `_BaseMap` 指向已销毁贴图。探索 8-12 个星系后必现，主菜单行星同缓存也中招。
- 修复：淘汰贴图时同步 `Materials.Remove` 并销毁对应材质（或贴图失效时重建）。

**7. [P2] 武器开火音效双音/错音：所有 Weapon 事件先播采矿音**
- `App/AppRoot.cs:533-545`：`evt.Type == Weapon` 无条件 `PlayMining()`，非采矿再叠 `PlayLaser()` → 每炮双音。
- 修复：按 detail 判定，采矿音与激光音互斥。

**8. [P2] 音乐 crossfade 线性曲线中间响度下陷**
- `Presentation/MusicDirector.cs:240-251` 线性交叉淡化中点两源各 50%。修复：等功率曲线（`sqrt(p)`/`sqrt(1-p)`）。

**测试**：EditMode 单测：贴图淘汰联动材质移除；SFX persist:false 不写盘。音效/crossfade 编译+设备听感验证。

## M3 — 战斗与机库体验（P2，模拟+表现）

**9. [P2] 车站维修不认被动件：装甲板/护盾扩展器加成永远修不满，甚至倒扣护盾**
- `GameSession.cs:1614-1653` `Repair()` 恢复到裸船体 `definition.HitPoints` 而非含被动件堆叠的 MaxShield/MaxArmor；装甲无修理器时永久缺口，护盾会被"维修"倒扣。装甲费不足时连船体维修一并拒绝、还提示 partially repaired。
- 修复：按实际上限修复；费用不足时按序部分维修（shield→armor→hull 各自独立判断）。

**10. [P2] 卖船连带吞掉船上已装配的全部模块**
- `GameSession.cs:1303-1324` `SellShip()` 直接 Remove，Fitting 里的模块（HeavyLaser 90k×N）不退机库不计价。
- 修复：卖船前卸下全部模块回机库（或计入卖价，取退回更直观）。

**11. [P2] 护盾回充/装甲维修没有自动循环，NPC 却有**
- `GameSession.cs:581-622`：玩家主动防御件是"点一下回一口"（`runtime.Active` 从不置位），NPC 却自动回充（`:872-876`）。手机战斗中每 8-10 秒戳一次按钮。
- 修复：ShieldBoost/ArmorRepair 激活后进入循环（Active 置位，UpdatePlayerModules 周期执行直到电容耗尽/再次点击关闭），与武器一致的开关语义。

**12. [P2] 敌对染色在视图复用期间不更新——3D 与总览威胁显示矛盾**
- `Presentation/SpaceWorldPresenter.cs:118-127,322`：hostile 红色调只在建视图时烘焙，M8 生态下 NPC 飞行中转敌对后 3D 不变红。
- 修复：Present 复用路径检测 `IsHostile` 变化，MaterialPropertyBlock 重染色。

**测试**：EditMode 单测：维修到含被动件上限+部分维修、卖船退模块、防御模块自动循环（多次 AdvanceFrame 断言回充量与电容）。染色编译+设备验证。

## M4 — 任务与进度公平（P2，模拟）

**13. [P2] 任务目的地完全不过滤安全等级**
- `GameSession.cs:1928-1952` `PickDestinationSystem` 只看"N 跳内有站"。L1 萌新被指向 0.0 海盗窝；L4 六条 BC 任务怪刷在 1.0 首都门口。
- 修复：目的地安等随任务等级收窄（L1-2 目的地 ≥0.5 / L3 ≥0.2 / L4 不限），找不到再放宽兜底。

**14. [P2] 任务怪被 NPC 海军抢杀且不计进度**
- `GameSession.cs:680` 击杀者是 NPC 时在 `OnMissionKill`（`:692`）之前直接 return。玩家 600m 索敌圈外狙击时任务怪与海军互啄，进度无提示卡死。
- 修复：任务标记实体被任何一方击杀都推进任务进度（击杀者是 NPC 时无赏金无声望，只计 kill）。

**15. [P2] 故事线任务难度不随触发等级缩放**
- `GameSession.cs:1574` 固定 `Level=3`、`:1587` KillsRequired=6 → L1 护卫舰飞行员收到精英 BC+5 巡洋的断崖任务，且 Offered 无过期、日志永久堆积。
- 修复：故事线等级=触发它的任务等级；KillsRequired 随等级缩放；Offered 任务加 7 天过期（存档已有时间戳可复用）。

**16. [P2] 非本国 LP 是死货币**
- `GameSession.cs:1655-1678` LP 商店只消费 `Player.EmpireId` 的 LP，但首都 Directorate、Sanctuary、Pirate Haven 代理发各自派系 LP（`:1503`），永远无处可花。
- 修复：LP 商店按当前停靠站派系的 LP 报价与扣减；UI 显示当前站派系 LP 余额。

**测试**：EditMode 单测：目的地安等约束、NPC 击杀计任务进度、故事线随等级、异派系 LP 可兑换。

## M5 — 移动端 UI 与本地化（P2/P3，UI 层）

**17. [P2] 英文模式 + 触屏设备上键鼠提示 [M]/[J]/[C]/[W]/[L] 残留**
- `UI/UiLocalizer.cs:66-84` `LocalizeValue` 只在中文分支调用 `NormalizeForDevice`，英文模式首次 Apply 直接跳过 → 切英文后 HUD 显示不存在的快捷键。
- 修复：英文分支捕获 SourceText 时同样过 `NormalizeForDevice`。

**18. [P2] 翻译缺键：LP 商店按钮、卖船模板**
- `UI/Station.uxml:30` "EXCHANGE 100 LP FOR DAMAGE AMP" 无 key；`App/AppRoot.cs:1002-1004` "SELL SHIP · {0}"/"Sell the ship · {0}" 不在 536 条翻译表 → 中文界面整行英文。
- 修复：补 L10n 条目并让按钮文案走 Tr（LP 兑换按钮改为带参数模板，避免写死 100/DamageAmp）。

**19. [P2] 技能页 TRAIN 按钮实为"取消训练"开关，无状态提示**
- `UI/StationUiController.cs:150,163` 按钮恒显 TRAIN；对队列中技能再点=静默取消（`GameSession.cs:295-307`），触屏误触代价高。
- 修复：队列中/训练中显示"停止训练"语义文案与不同样式。

**20. [P2] 总览预设 Tab 与排序列头重叠，compact 误触**
- `UI/SpaceHud.uss:20-24,39-47,206-207`：容器 33px/25px 高但按钮 40px，排序列后绘制在上层 → 点 Tab 下半实际触发排序。compact 重叠 ~12-15px。
- 修复：容器高度补齐 + 命中区分离。

**21. [P3] 站内 6 Tab 英文截字、无 compact 规则**
- `UI/Station.uss:6-8`：480px 面板平分 ~73px/tab，"LP STORE" 逼近溢出；整个 Station.uss 无 compact 段。
- 修复：tabbar 字号/内边距 compact 化，必要时缩写。

**22. [P3] 模块按钮首行数字是键鼠残留**
- `UI/SpaceHudController.cs:303,309` `$"{i+1}\n{name}"` 序号对应 PC 数字键；触屏上是噪音且挤占 48px 高按钮。修复：触屏设备不显示序号（与 dock 按钮 [D] 剥离策略一致）。

**23. [P3] 技能训练进度百分比在站内冻结**
- `App/AppRoot.cs:1027-1047` 进度只在列表重建时计算；技能点每帧累积但升级前不发事件，停靠时 telemetry 又跳过 → 玩家盯着百分比不动。修复：训练中技能周期性（~1s）轻量刷新进度行。

**测试**：EditMode UI 单测（Localizer 剥离、模板翻译键存在性校验沿用 L10nTests 模式）+ 编译；设备截图验证 compact。

## M6 — 性能与稳健性（P2/P3，App/Presentation）

**24. [P2] ContinueGame 异常过滤不完整——一档坏档使整个"继续游戏"静默失败**
- `App/AppRoot.cs:264-279` catch 只滤 IOException/InvalidData/InvalidOperation；`ToObject<PlayerState>` 的 JsonSerializationException、目录 ID 漂移的 KeyNotFoundException 会中断 foreach，后面的槽不再尝试。修复：逐槽降级（catch 对齐 `FileSaveService.IsSaveReadFailure` 集合），失败槽跳过。

**25. [P2] 长按调度项泄漏**
- `UI/WorldBackdropInput.cs:69-73,91,109,149` 每次 PointerDown 新建一次性 schedule 项，取消只 `Pause()` 不移除 → 飞行会话内单调累积，调度器每帧遍历。总览行 `SpaceHudController.cs:768-776` 同模式。修复：复用单个可重置调度项。

**26. [P2] 池化光束每次开火 `.material` 赋值——材质实例累积**
- `Presentation/SpaceWorldPresenter.cs:166` LineRenderer 用实例化语义的 `.material`，持续炮战稳定累积；每发还拼 `beam-XXXXXX` key。修复：改 `sharedMaterial`（材质已按颜色缓存）+ Color 缓存 key。

**27. [P2] 存档里的装配模块 ID 不做目录校验——未来目录删改件旧档必崩**
- `GameSession.cs` `NormalizeSkills`（`:166-181`）清洗了技能，但 Fitting 里未知 moduleId 在 `ActivateModule:569`/`UpdatePlayerModules:610`/`RecomputeDerived:1215` 直接 KeyNotFoundException。修复：读档时剔除未知模块 ID（记日志）。

**28. [P3] 三指触控 pinch 取"任意两指"——距离跳变、镜头猛拉**
- `UI/WorldBackdropInput.cs:172-190`、`StationBackdropInput.cs:102-120` 遍历字典取前两个值。修复：BeginPinch 时锁定两指 pointerId，后续只测这两指。

**29. [P3] 每帧 `SceneManager.GetActiveScene().name` 托管分配**
- `App/AppRoot.cs:217` 每帧封送新字符串。修复：sceneLoaded 时缓存。

**30. [P3] WorldBackdropInput 无 Dispose 且 OnEnable 无条件重建（与 Station 侧不对称）**
- `UI/SpaceHudController.cs:81-87,108-123`。修复：对齐 StationBackdropInput 的 Dispose+判空复用；顺手处理 MainMenuUiController 重复注册与设置面板 PollAndroidBack 泄漏。

**31. [P2] 战斗频率事件全量 UI 列表重建**
- `App/AppRoot.cs:493-514` Spawn/Despawn/Death 都 `MarkUiDirty(true)` → 每次重建市场/技能/星图全表。M8 NPC 混战下每秒多次。修复：按事件类型拆分，Spawn/Despawn/Death 只刷 Overview+telemetry，不重建站内列表。

**测试**：EditMode 单测：存档模块 ID 清洗、Continue 逐槽降级。其余编译+设备验证。

## 未排期（记录在案，本轮不做）

- Buy 校验失败也推高市场压力（`GameSession.cs:1243` 先加压后拒绝）。
- 市场压力 / directorateSpawned 不进存档（`:29-31` 会话字段）。
- Navigation 技能训练完成不即时刷新当前速度（`GameSession.cs:1227-1228`）。
- 海盗派系代理人发"杀自己派系"任务自相矛盾（`:1462-1463`）。
- 同速追逐永不结束/商船无跃迁离场（`:865,766-769`）。
- Enforcer 全灭后犯罪窗口内无后续响应（`:361,1728`）。
- Android 音频焦点（来电/分屏闪避）、AudioMixer 总线。
- 机库 accent 直接改写共享缓存材质（`StationHangarPresenter.cs:402-414`）+ ProceduralShipFactory 材质缓存无上限。
- pilot-summary 未走本地化且用 CurrentCulture（`StationUiController.cs:98`）。
- compact 总览行高 34px 与 48px 触控基线冲突（密度取舍，暂留）。
- 实体 20Hz 步进无渲染插值（高刷屏 judder，改动面大，独立排期）。
- 主线程同步序列化存档 hitch（异步化改动面大，独立排期）。
- #39 占位几何 / #41 空间站内部 / #53 星系重叠兜底（沿用上轮结论：需美术/存档迁移前置）。

---

## 实施进度

- [ ] M1 存档完整性与经济公平（#1-3）
- [ ] M2 设置、音频与渲染缓存（#4-8）
- [ ] M3 战斗与机库体验（#9-12）
- [ ] M4 任务与进度公平（#13-16）
- [ ] M5 移动端 UI 与本地化（#17-23）
- [ ] M6 性能与稳健性（#24-31）
