# TODO（2026-09-14 code review：1.4.0 第二轮 M3–M6，2201701..f0ee974）

Review 范围：`git log 925f564..f0ee974`（M3 战斗机库 / M4 任务公平 / M5 移动端 UI 与本地化 / M6 性能与健壮性 + 两个记账 commit）。

**已当场修复的高风险问题**（见同日工作区改动，附回归测试 `OverviewContactBuilderTests`，EditMode 192/192 绿）：

- M6 把 Spawn/Despawn/SystemPopulated 降级为轻量刷新、Death 也移入轻量组后，overview 联系人结构不再重建：被击毁/跳跃离场的 NPC 留下幽灵行（距离 -1），新生成的 NPC（犯罪引来 Directorate 回应舰队、600s 周期的 NPC 交通重生）不出现在列表里，直到下一次 dock/jump/inventory 才自愈。修复：`AppRoot` 增加 `overviewShipsDirty` 标志（Spawn/Despawn/Death 置位），轻量刷新路径先 `OverviewContactBuilder.ReconcileShips`（原地增删 Ship 类联系人）再 `RefreshTelemetry`；全量重建路径不受影响，M6 的市场/技能/星图列表缓存收益保留。

## 低风险待办

1. **损坏存档下 CurrentSystemId 未做归一化** — `AppRoot.BuildUiSnapshot` 两处（含 M4 新增的 LP 阵营查找）与 `GameSession.CurrentSystem()` 都直接 `Universe.Systems[player.CurrentSystemId]`。停泊状态存档若带失效星系 id，会在每帧快照里 KeyNotFoundException。已有 `NormalizeFittings`/`NormalizeMissions` 的先例，可在加载时对 CurrentSystemId 做同样的回退（回落母星系）。

2. **Continue 的 catch(Exception) 吞掉了非损坏类 bug** — `AppRoot` Continue 循环现在捕获所有异常并显示"无有效存档"。`Debug.LogException` 有栈，但没有标注是哪个槽位失败，排障时难以对应文件。建议在每个槽位的 catch 里加一行带 slot id 的日志。

3. **StationBackdropInput.Dispose 不清 `pointers`/捏合指针对** — `WorldBackdropInput.Dispose` 清了，站点侧没清。回调已注销且有 `disposed` 守卫，无实际影响，仅一致性问题。

4. **UiLocalizer 对动态文本的触屏提示剥离限制** — 只有首次 Apply 时捕获的源文本（或恰好被控制器写回源文本的）会被剥 "[M]" 提示/翻译；控制器写入的动态英文字符串不会被处理（与中文行为一致）。将来若把热键提示拼进动态字符串，需要改为调用点自行 `NormalizeForDevice` 同等逻辑。

5. **小行星"剩余数量"文本依赖 Inventory 事件刷新** — 轻量路径 `RefreshTelemetry` 不更新烘进行文本的 units 字符串；目前靠 `Mine()` 每周期发 Inventory（全量重建）自愈。改 `Mine()` 事件发射时注意保持这条耦合。
