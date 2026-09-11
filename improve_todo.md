# STARFALL ODYSSEY 改进计划

> 生成日期：2026-09-11。来源：对 `UnityProject/Assets/Starfall` 全部脚本的逐行审查（Simulation / UI / App / Presentation / Persistence）、ProjectSettings 与 QualitySettings 核对、Android 实机截图与构建日志复核。
> 范围：Unity Android 版（主力交付物）。Web 原型（js/）不在本期范围内。
> 行号基于当前工作区代码，均已抽查验证，非推测。

---

## P0 — 致命：目标平台上游戏不可玩 / 丢数据 / 核心循环崩溃

### 1. 触屏输入完全缺失，手机上无法操作 3D 世界
- 全项目 0 处 `Touchscreen` / `EnhancedTouch` 引用（已 grep 验证）。目标选取、双击接近、右键菜单只读 `Mouse.current`：`Presentation/SpaceWorldPresenter.cs:267-304`；相机旋转=右键拖拽、缩放=滚轮：`Presentation/EveCameraController.cs:66-87`；快捷键全部 `Keyboard.current`：`App/AppRoot.cs:398-416`。
- Android 上 Input System 不会把触摸合成为鼠标，`Mouse.current` 为 null → 点击选中/接近/旋转/缩放在真机上全部失效，只剩 UI 按钮。
- **修复**：接入 EnhancedTouch；单指拖拽=轨道旋转、双指捏合=缩放、轻点=选中（参考 `SpaceWorldPresenter.cs:290` 已有的 `delta.sqrMagnitude < 12f` tap-slop 判定）、长按=上下文菜单；为触屏补"接近/锁定/跳跃"的屏上按钮。这是发布前的第一优先级。

### 2. 切后台/杀进程丢进度：无暂停存档钩子
- 全项目 0 处 `OnApplicationPause` / `OnApplicationFocus` / `OnApplicationQuit`（已 grep 验证）。自动存档只发生在 dock / undock / jump / respawn（`Simulation/GameSession.cs:316,340,360,1198`）。
- 采矿收益、买卖、装配、任务接受/完成、击杀、掉落在没回到空间站前全部不落盘。Android 上来电/切后台被系统回收是常态 → 10 分钟白挖。
- **修复**：`OnApplicationPause(true)` 时同步存档（存档本身是原子写，`Persistence/FileSaveService.cs:69-89` 质量没问题）；另加周期性自动存档（如每 60s 或每 N 次模拟事件）。

### 3. 死亡不存档 + 货物不随船损失 → 杀进程即可撤销死亡，死亡几乎无惩罚
- 玩家死亡 `Kill()` 只发事件不触发存档：`Simulation/GameSession.cs:452-463`；`PlayerDead` 不在存档信封里：`Persistence/SaveEnvelopeV2.cs:62-80`。死亡→上划杀 App→重进，船、货、钱全在。
- 货物挂在 `PlayerState` 而非船上（`Simulation/GameState.cs:114`），`Kill()/Respawn()` 只删船不动货（`:1175-1199`）→ 押送任务死了照常交货， hauling 零风险。
- **修复**：死亡立即写档；死亡掉货（或保险/残骸回收机制）；`PlayerDead` 进存档。

### 4. 每次停靠/离站重置小行星与 NPC → 无限矿石和赏金水龙头
- `Undock()` 调 `PopulateSystem()`（`Simulation/GameSession.cs:330-341`），清空并满血重刷小行星（`:664,683`）、移除重生成全部 NPC（`:663,697-718`）。小行星余量与 NPC 状态从不持久化。
- 挖空矿带 → 停靠 → 离站，全部回来；海盗赏金同理。核心经济被无限 AFK 资源击穿，也是货币通胀的最大水龙头。
- **修复**：持久化小行星余量与 NPC 存活状态（至少按系统记录"已枯竭/重生计时"）；离站不重置已访问系统。

### 5. Android 默认跑 PC 画质档
- `ProjectSettings/QualitySettings.asset:117`：`Android: 1` = "PC" 档（档 0 才是 Mobile）；且无用户偏好时 `ApplySavedQuality()` 回落到名为 "PC" 的档：`App/AppRoot.cs:359-368`。
- PC 档代价：renderScale 1.0、深度+OpaqueTexture 两张全屏拷贝、2048 主阴影 4 级联+软阴影、每灯阴影 2048（`Assets/Settings/PC_RPAsset.asset`）；再加 lodBias 2。新装机在中端手机上直接重管线。
- **修复**：Android 平台默认档改 Mobile；首次启动按设备档位（SystemInfo）选择。

### 6. 存档异常未捕获 → 单次 IO 失败闪退整个游戏
- `App/AppRoot.cs:916-939` `Save()` 无 try/catch，磁盘满/存储不可用时 IOException 从 `Update()` 冒泡直接杀进程（对比 `ImportLegacy` 在 `:272-276` 是有保护的）。
- **修复**：包 try/catch + 失败提示（配合 P1-14 的 toast 系统）。

---

## P1 — 严重：明显伤害玩家体验的性能 / UX / 平衡问题

### 移动端 UI

7. **触控目标远低于 48dp 标准**（设计分辨率 1920×1080 下的像素值，物理尺寸更小）：Overview 行 30/26px（`UI/SpaceHud.uss:89-96,213`、`UI/StarfallResponsiveUi.cs:16-17`）；预设 tab 与排序头 28/23px（`SpaceHud.uss:20-30,47-67,205,207`）；compact 模块栏（主战斗控件）min-height 0、9px 字体（`SpaceHud.uss:236-245`）；顶栏按钮 26px（`:200`）。统一抬高到 ≥48px 并放大 compact 字号。
8. **Overview 按下即选中，拖动滚动会误选目标**：`UI/SpaceHudController.cs:698,756-759` 在 `PointerDownEvent` 里发 select；改为 `PointerUpEvent` + 位移阈值（tap-slop）。另有 ListView `selectionType=Single` 与自定义 select 双轨（`:176`）。
9. **无安卓返回键处理**（0 处 Escape/back）：设置/星图/日志浮层只能点屏上按钮关，系统返回手势会直接退 App。补 `Keyboard.current.escapeKey` → 关闭最上层浮层。
10. **无 safe area / 刘海适配**（0 处 `Screen.safeArea`）：HUD 面板距边 10-16px，会顶进挖孔/状态栏；`SpaceHud.uss:195-196` 还留着给"微信 webview 胶囊"硬编码的 132px 顶栏 inset，原生构建白白浪费一行空间。删残留 inset + 接 `Screen.safeArea`。
11. **竖屏允许但 UI 为横屏设计**：`ProjectSettings/ProjectSettings.asset:62-65` 四方向全开；竖屏 compact 下 overview（bottom 272px）+ 模块栏（206px）+ 战斗日志（10px）+ 132px inset 叠加后列表只剩约 300px 且任务栏被压。建议锁横屏，或做真正的竖屏堆叠布局 + 相机 FOV 补偿。
12. **滑条每 tick 写盘**：UI 缩放滑条 `UI/StarfallSettingsPanel.cs:168-172` → `App/AppRoot.cs:332-333` 每次变更 `PlayerPrefs.Save()`；音量同理 → `Presentation/MusicDirector.cs:111-112`。拖动期间在闪存上反复 fsync，必卡。改为 `PointerUpEvent` 时持久化。
13. **无加载画面/过渡**：dock/undock/jump/死亡全部同步 `SceneManager.LoadScene`（`App/AppRoot.cs:517-522`），真机上多次帧冻结且无反馈。改 `LoadSceneAsync` + 过渡遮罩（配合 P2 的跳跃特效）。

### 反馈与安全

14. **主菜单错误零反馈**：CONTINUE 无存档、IMPORT 失败只写 `AddLog`（`App/AppRoot.cs:239-276`），而主菜单 UI 从不显示日志（`MainMenuUiController.cs` 未绑定）→ 点了没反应。全项目没有任何 toast/对话框组件，需要先建这个基础设施。
15. **破坏性操作无确认**：legacy 导入直接覆盖 Slot1（`App/AppRoot.cs:268`）；开始新游戏立即覆盖 auto 档（`:210-220`）；重生按钮直连执行（`SpaceHudController.cs:147`）。
16. **Continue 不看时间戳**：`App/AppRoot.cs:225` 按 Auto→Slot1→2→3 取第一个可读档，Auto 永远赢过更晚的手动档。应比较 `SaveSlotInfo.LastWriteTimeUtc`（字段已存在：`Persistence/SaveContracts.cs:61`）。

### 性能（中端 Android 卡顿来源）

17. **每帧 GC 分配**：未停靠时每帧 `worldDirty=true` → `PresentSpace()` 全量跑（`App/AppRoot.cs:171-174`）；`BuildSpaceSnapshot()` 每帧 new 快照+逐对象 `WorldObjectViewData`+字符串拼接（`:542-600`）；`SpaceWorldPresenter.Present()` 每帧清填字典（`SpaceWorldPresenter.cs:46-79`）。改为脏标记真正按变化增量刷新。
18. **跳跃卡顿**：进新系统在主线程 CPU 循环 1536×768 像素生成天空贴图（约 7MB 托管分配 + 每像素 ~10 次 Sin）：`Presentation/ProceduralSpaceMaterials.cs:283-351`。降分辨率/改 GPU 生成/异步 + 缓存预热。
19. **零对象池**：NPC 视图 ~15 个 primitive 子对象随 spawn/destroy 反复建拆（`SpaceWorldPresenter.cs:214-265`）；激光每发 new LineRenderer（`:98-112`）、爆炸每次 new ParticleSystem 从零配（`:114-136`）。持续战斗=分配风暴。补池化。
20. **UI 热路径分配与全量重建**：10Hz telemetry 每联系人重排字符串（`SpaceHudController.cs:792-810`）+ dock 按钮文本每 tick 重赋值（`:358`）；每次 SnapshotChanged 战斗日志 string.Join + 9 个模块按钮字符串重建（`:257-279`）；`StationUiController.Fill` 在每次 SnapshotChanged 无指纹守卫地清空重建四个列表的全部行（`StationUiController.cs:92-106`，对比 HUD 已有守卫的 `RefreshCachedList` `:585-609`）；`SetText/SetBar` 每次全树 `Q<>` 查找（`:568-583`）。缓存引用 + 指纹守卫 + StringBuilder。
21. **星光点光源浪费**：每颗星一个 range≥500、intensity 4 的像素光（`SpaceWorldPresenter.cs:233-237`），星体本身是自发光 unlit 球，纯浪费。删掉或降级。
22. **无限增长的缓存**：星球材质/贴图字典只进不出（`ProceduralSpaceMaterials.cs:208-237,353-413`，仅天空有 8 上限 `:453-463`）；三处每场景 `NewInstance<VolumeProfile>` 不销毁（`SpaceWorldPresenter.cs:195`、`StationHangarPresenter.cs:269`、`MainMenuBackdropPresenter.cs:133`，运行时创建的 ScriptableObject 不随场景卸载）。长会话内存只涨不跌。

### 游戏逻辑 / 平衡

23. **静态价格 + 全局机库 = 无限套利**：`StationPrice()` 是 (station,item) 哈希×治安系数的纯函数（`Simulation/GameSession.cs:1240-1252`），买卖无库存无供需；模块躺在全局机库零成本瞬移（`GameState.cs:115`）；高安保买入→海盗港卖出约 60% 无风险差价永续循环（`:856-899`）。至少加：价格随交易量漂移、库存上限、卖出折价收紧。
24. **免费瞬时维修 + 瞬时停靠**：`Repair()` 无任何费用（`GameSession.cs:1144-1154`），40m 内随时秒进站（`:294-318`，无 EVE 式 60s 武器锁定）。战斗→进站→白修→出站无限循环，护盾回充/装甲维修机模块彻底无用，经济也少一个重要 ISK 汇。维修收费 + 进站武器计时。
25. **同城任务秒完成刷钱**：`PickDestinationSystem()` 候选含出发系统本身（`:1400-1421`），选不中目标站时回落到代理人本站（`:980`）→ 接任务→出站→进站，`CompleteDockObjectives` 直接收 18000×等级 ISK+LP+声望（`:1065-1071`），可对每个代理人无限重复。`TalkToAgent` 还强制立即接受所有报价（`:958`）。
26. **任务货物可被卖掉导致任务卡死**：`Sell()` 不检查 `NoMarket`（`:882-891`），密封货物按 1 ISK/单位清仓后交付目标永远无法完成，只能放弃扣声望（`:1055`）。卖货时过滤 `noMarket` 物品。
27. **太空里接受任务会重置整个系统**：`AcceptMission` 未停靠时调 `PopulateSystem()`（`:1026`）→ 正在打你的 NPC 全部消失、小行星重置，既是逃战漏洞又很突兀。
28. **警察永不撤岗**：Directorate 响应带 `AggroRange 99999` 预锁定（`:1206-1213`），而 AI 只在无目标时重评关系（`:606-612`）→ 犯罪计时器归零后仍被永久追杀；反之停靠后 `directorateSpawned` 不复位（`:313` vs `:358`）又不再刷新。双向不一致。
29. **新手会被驱逐舰级海盗劝退**：帝国星系 30% 矿带遭遇海盗（`:699-700`），`SpawnPatrol` 船体掷骰可出 index 1 驱逐舰（~550/600/450 EHP×3 武器）甚至 index 2 巡洋（`:730`），而初始护卫舰仅 ~300/320/280×1-2 武器（`Content/GameContentCatalog.cs:72-92`）。按治安/玩家实力约束刷怪船体档位。
30. **存档健壮性**：版本不匹配严格抛异常无迁移路径（`Persistence/SaveEnvelopeV2.cs:87-95`），未来生成器一升版所有旧档变砖；legacy 导入在 Android 上扫 Downloads 目录注定失败（沙箱），只有 `persistentDataPath/legacy-v1.json` 可行（`App/AppRoot.cs:1041-1043`）；`Respawn()` 对 `station.Position` 无空检查（`:1191-1192`），跨版本档直接 NRE。
31. **音频层只有音乐没有声音**：仅两路音乐 AudioSource（`Presentation/MusicDirector.cs:17`），武器/爆炸/对接/UI 点击/引擎/迁跃全静音；无 AudioMixer/总线/闪避。且无 Android 音频焦点处理（来电不闪避不暂停），crossfade 与音量是线性曲线（`:233-251,106-115`），应有等功率/对数曲线。SFX 是"像不像游戏"的第一道坎，优先级很高。

---

## P2 — 3A 品质差距：深度系统与视听表现

### 玩法深度（EVE 之魂）

32. **无技能系统**：进度=纯 ISK，无技能训练、无船体门槛——第一天就能开 900 万战列舰（`GameContentCatalog.cs:75-90` 无 NpcOnly）。这是 EVE 重制最大的缺失支柱，也把升级曲线完全压平。建议最小实现：技能队列（离线训练，天然契合移动端 + 已有固定步长模拟 `GameSession.cs:139-154`）+ 船体/模块技能门槛。
33. **无装配约束**：`Fit()` 只查槽位类型与空位（`:901-916`），无能量栅格/CPU/模块尺寸——护卫舰装重列激光；无堆叠惩罚——6×DamageAmp 乘法叠到 2.70 倍伤害、6×护盾扩展 +1200（`:843-849`）。
34. **NPC AI 浅且只针对玩家**（`:597-651`）：海军与海盗和平共处，无 NPC vs NPC 生态；行为只有接近/绕行/逃跑消失；无电子战、无人机、电容、弹药、伤害类型与抗性。
35. **任务系统无时限、无失败、无回程要求**：安全任务在任意星系都能点完成（`CompleteMission` 无站点检查 `:1029-1046`）；`MissionState.AmbushSpawned` 是死代码——押送伏击从未实现（`GameState.cs:84`）。
36. **LP 商店是残桩**：仅"100 LP→1 个 DamageAmp"且只认玩家本国阵营（`:1156-1173`）；**船永远不能卖**（`:885-898`），每次死亡送的新手船在机库无限堆积（`:1180-1186`）；机库/货物全宇宙瞬移，无物流玩法。
37. **声望几乎无后果**：只在 ±5 翻 NPC 关系（`EntityDispositionPolicy.cs:56-69`），无站点拒入、代理人锁、声望衰减。
38. **内容天花板低**：代理人等级被星系治安硬封 3 级（`UniverseGenerator.cs:292-294`）；船树 4 艘可购战列到顶；经济水龙头巨大（赏金/任务/矿石）汇几乎为零（维修免费、无弹药/燃料/保险）。故事线任务 1.2-1.5M ISK vs 普通任务 90k（`:1009,1106-1109`）13-16 倍断层。

### 视听表现

39. **占位几何**：飞船=胶囊脊柱+方块机翼+圆柱引擎的 primitive 拼装（`ProceduralShipFactory.cs:34-90`）、空间站/星门同理；行星表面 256×128 正弦贴图，无大气/云/晨昏线（`ProceduralSpaceMaterials.cs:353-413`）。
40. **无战斗与航行 VFX 反馈**：武器=0.12s 的单条 LineRenderer，无弹丸/枪口闪光/命中（`SpaceWorldPresenter.cs:98-112`）；爆炸=单个默认 burst 粒子，无冲击波/残骸/声音（`:114-136`）；无引擎尾焰（只有静态发光球 `ProceduralShipFactory.cs:68-70`）；跳跃/迁跃无任何特效，场景硬切（Jump 事件只标脏 `App/AppRoot.cs:434-437`）——无隧道、无闪光、无镜头震动；受击数据存在（Shield01/Armor01/Hull01 `App/AppRoot.cs:594-596`）但模型上完全不可见——无受击闪白、护盾泡、损伤状态。
41. **空间站内部是静态盒子**：发光条的盒子几何+旋转的船，无 NPC、无环境动画、无环境音（`StationHangarPresenter.cs:45-216`）。
42. **相机与渲染细节**：near/far = 0.15/12000（80,000:1）在 24bit 移动深度上有 z-fighting 风险（`EveCameraController.cs:32-33`）；`CameraRenderQuality.cs` 在 URP 下是无效死代码（allowHDR/allowMSAA 被管线资产接管）；重置相机视角硬切无缓动（`:53-58`）。
43. **无障碍缺失**：敌我判定仅靠红/绿颜色，图标几何相同（`OverviewIconElement.cs:85-98`）；盾/甲/结构=蓝/橙/红三色条；无 disabled 态 USS 样式（四个 .uss 里 0 处），禁用按钮与可用按钮几乎同貌；关键信息（行详情、模块说明）只在 hover tooltip 里，触屏用户永远看不到（`SpaceHudController.cs:603,723`）——需要长按详情面板。无手柄支持（0 处 Gamepad）。

---

## P3 — 打磨与工程卫生

44. **本地化缺口**：LP 兑换按钮硬编码英文（`Station.uxml:28` 无对应 key）；速度 "k" 后缀未本地化（`SpaceHudController.cs:808`）；触屏设备上仍显示 `[W]/[L]/[D]/[M]/[J]/[C]` 键鼠提示（`SpaceHud.uxml:9-11,44-48,76,86`，且 `:358` 每 tick 重赋值）、帮助文案讲的是"点击与 W/L/D 键"（`App/AppRoot.cs:408`）。按输入设备切换标签。
45. **中文列宽溢出**：compact 速度列 46px 装不下 "999 米/秒"、距离列 52px 装 "12.3 千米"（`SpaceHud.uss:218-220` vs `L10nData.Ui.cs:102,106`），ellipsis 会吃掉单位。中文模式放宽列宽或换单位符号。
46. **UI 缩放逻辑反直觉**：缩小 UI（scale<1）反而触发更紧凑模式，字更小（`StarfallResponsiveUi.cs:33-46`）；`ApplyUiScale` 只缩放找到的第一个 UIDocument（`App/AppRoot.cs:340-344`）。
47. **杂项逻辑小虫**：逃跑计时双跳——海盗 ~2s 就"迁跃离开"而不是 4s（AiTime 在 `GameSession.cs:605` 与 `:643` 双加）；afterburner 读 catalog 不读实际装配（`:850`）；`VisitCounter`/`directorateSpawned`/warp 中存档的目标不持久（`:24,107-129,124-128`）；`SwitchShip` 不校验货舱容量（`:932-939`）；`AdvanceFrame` 负 delta 直接抛异常无恢复（`:142-143`）；死代码——无人发送的 "settings" Execute 路径（`App/AppRoot.cs:281-285`）。
48. **新手引导摩擦**：初始舰高槽双武器装满，送的采矿激光躺在机库里（`GameSession.cs:88-92`）——矿工第一件事是拆炮装工具。出生装配给矿留槽。
49. **存档工程细节**：Indented JSON 更大更慢（`FileSaveService.cs:18`）；同步序列化+fsync 在主线程，每次 dock/jump 都 hitch（`:56-89,222-231`）；"Game saved to auto." 每次刷日志（`App/AppRoot.cs:938`）；`ReadSlotInfo` 重复完整验证两次（`:158,190-201`）。
50. **仓库卫生**：`UnityProject/.utmp/**` 构建中间产物与 `build/android/*.apk`（90MB 二进制）被 git 跟踪，工作区常年脏（见 git status）；建议 .gitignore 收口并 `git rm --cached`。
51. **ProjectSettings 残留**：`accelerometerFrequency: 60` 但无代码读传感器（白白轮询耗电，`ProjectSettings.asset:14`）；desktop 分辨率/targetDevice 残留（`:45-46,12`）。
52. **星图 O(N×M)**：每次全量 UI 重建对每个星系跑一次寻路（`App/AppRoot.cs:773-781`），缓存路线结果。
53. **星系生成重叠无兜底**：40 次拒绝采样耗尽后静默接受重叠星系（`UniverseGenerator.cs:105-110`）。

---

## 建议的实施批次

| 批次 | 内容 | 理由 |
|---|---|---|
| 第一批（可玩性） | #1 触屏输入、#2 暂停存档、#6 存档异常保护、#16 Continue 时间戳 | 没有这些，Android 上"不可玩且不可信" |
| 第二批（核心公平） | #3 死亡惩罚、#4 资源重置、#23-25 经济/任务漏洞、#29 新手刷怪 | 不堵住，任何进度设计都无意义 |
| 第三批（手感与性能） | #7-12 触控目标/误选/返回键/safe area/竖屏/滑条写盘、#17-22 GC/池化/跳跃卡顿、#13 加载画面、#31 SFX | 决定"像不像一个正经手机游戏" |
| 第四批（3A 化） | #32-38 技能/装配/AI/任务深度、#39-43 VFX/无障碍 | 从"能玩"到"值得玩" |
| 第五批（打磨） | #44-53 | 发布前清扫 |

> 注：代码中刻意保持了与 JS 原型的 bug 级兼容（`UniverseGenerator.cs:9`、`Domain/Random.cs:16-17`、`EntityDispositionPolicy.cs:32-33` 等注释自述"逐 bug 兼容"）。以上游戏逻辑类条目在修复前需逐条确认：哪些是"打算保留的原型行为"，哪些是应当修掉的缺陷。
