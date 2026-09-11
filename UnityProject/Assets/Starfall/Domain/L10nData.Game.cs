namespace Starfall.Domain
{
    internal static partial class L10nData
    {
        // Simulation events, mission text, app-layer log lines and list rows.
        // Format keys use {0}/{1} placeholders filled by L10n.Tr with the English
        // canonical values (proper nouns are transliterated at the call site).
        private static readonly (string en, string zh)[] GameEntries =
        {
            // --- GameSession: command feedback ---
            ("Manual save requested.", "已请求手动保存。"),
            ("Warp drive active.", "跃迁引擎已启动。"),
            ("Warp drive deactivated.", "跃迁引擎已关闭。"),
            ("Only ships can be locked.", "只能锁定舰船。"),
            ("Target is outside lock range.", "目标超出锁定范围。"),
            ("Target locked: {0}.", "已锁定目标：{0}。"),
            ("Move within 40 m before docking.", "请靠近至 40 米内再停靠。"),
            ("Move within 35 m before jumping.", "请靠近至 35 米内再跳跃。"),
            ("Docked at {0}.", "已停靠于{0}。"),
            ("Undocked from {0}.", "已从{0}离站。"),
            ("Jump complete: {0}.", "跳跃完成：{0}。"),
            ("Shield restored.", "护盾已恢复。"),
            ("Armor restored.", "装甲已恢复。"),
            ("Docking is locked while weapons are hot. Try again in {0} s.", "武器刚开过火，停靠已锁定。{0} 秒后重试。"),
            ("Repair requires {0} ISK.", "维修需要 {0} ISK。"),
            ("Armor repaired for {0} ISK.", "装甲已维修，花费 {0} ISK。"),
            ("Hull repaired for {0} ISK.", "结构已维修，花费 {0} ISK。"),
            ("No repairs are needed.", "无需维修。"),
            ("{0} partially repaired.", "{0}已部分维修。"),
            ("{0} repaired.", "{0}已完成维修。"),
            ("This item has no market value here.", "此物品在此处没有市场，无法出售。"),
            ("Your cargo was lost with the ship.", "货物随舰船一同损失了。"),
            ("Mission failed: {0} — the cargo was lost with the ship.", "任务失败：{0}——货物随舰船一同损失。"),
            ("{0} destroyed.", "{0}已被击毁。"),
            ("Your {0} was destroyed!", "你的{0}被击毁了！"),
            ("Bounty: +{0} ISK for destroying {1}.", "赏金：击毁{1}，获得 +{0} ISK。"),
            ("Cargo hold full!", "货舱已满！"),
            ("Asteroid depleted.", "小行星已采尽。"),
            ("{0} has engaged you!", "{0}已向你开战！"),
            ("Hostile warped away.", "敌舰已跃迁撤离。"),
            ("{0}: {1} traffic contacts.", "{0}：{1} 个通行目标。"),
            ("Target lost. Active modules deactivated.", "目标丢失。已停用所有激活的模块。"),
            ("CRIMINAL ACT! The Directorate has been alerted.", "犯罪行为！理事会已被惊动。"),
            ("Directorate response units have warped in!", "理事会快速反应部队已跃迁抵达！"),

            // --- GameSession: trade, fitting, repair, loyalty ---
            ("Insufficient credits or item unavailable.", "信用点不足或物品不可用。"),
            ("Purchased {0} for {1} ISK.", "已购买{0}，花费 {1} ISK。"),
            ("Purchased {0}.", "已购买{0}。"),
            ("Sold {0}x {1}.", "已出售 {0}x {1}。"),
            ("Sold {0}.", "已出售{0}。"),
            ("Fitted {0}.", "已装配{0}。"),
            ("Unfitted {0}.", "已卸下{0}。"),
            ("Active ship: {0}.", "当前舰船：{0}。"),
            ("{0} fully repaired.", "{0}已完成维修。"),
            ("Insufficient loyalty points. The module cache requires 100 LP.", "忠诚点不足。模块库需要 100 LP。"),
            ("Exchanged 100 LP for {0}.", "已用 100 LP 兑换{0}。"),

            // --- GameSession: missions ---
            ("{0} — {1}", "{0}——{1}"),
            ("Mission offered: {0}", "任务发布：{0}"),
            ("Mission accepted: {0}", "已接受任务：{0}"),
            ("Mission complete: {0}. +{1} ISK, +{2} LP.", "任务完成：{0}。+{1} ISK，+{2} LP。"),
            ("Mission abandoned: {0}", "已放弃任务：{0}"),
            ("Objectives complete: {0}", "目标达成：{0}"),
            ("STORYLINE MISSION available: {0}", "故事线任务可用：{0}"),
            ("Need {0} m3 free cargo space.", "需要 {0} 立方米的空余货舱。"),
            ("No route available.", "无可用航线。"),
            ("Route set: {0} jumps to {1}.", "航线已设定：{0} 跳前往{1}。"),
            ("The Directorate issued you a rookie frigate.", "理事会向你发放了一艘新手护卫舰。"),
            ("Clone activated at home station.", "克隆已在母站激活。"),

            // --- Mission titles and descriptions (stored canonically in saves) ---
            ("Courier: Sealed Dispatch", "递送：密封急件"),
            ("Deliver encrypted cargo without opening the container.", "送达加密货物，切勿开启容器。"),
            ("Industrial: Ore Requisition", "工业：矿石征购"),
            ("Mine ore and return it to the agent's station.", "开采矿石并送回代理人的空间站。"),
            ("Security: Clear the Deadspace", "安保：清剿死亡空间"),
            ("Destroy the hostile squad threatening local traffic.", "歼灭威胁当地航路的敌军小队。"),
            ("Storyline: Breaking the Blockade", "故事线：突破封锁"),
            ("Eliminate an elite blockade squad.", "消灭一支精锐封锁小队。"),
            ("Storyline: The Ambassador", "故事线：大使"),
            ("Deliver a dignitary under absolute secrecy.", "在绝对保密下护送一位要人。"),

            // --- GameState.ProgressText ---
            ("Hostiles destroyed: {0}/{1}", "已击毁敌舰：{0}/{1}"),
            ("Deliver the sealed cargo to the destination station", "将密封货物送达目的地空间站"),
            ("Deliver {0} units of ore to the agent", "向代理人交付 {0} 单位矿石"),

            // --- AppRoot: welcome, help, save/load, import ---
            ("Welcome to the stars, {0}.", "欢迎来到群星之间，{0}。"),
            ("Talk to an agent, undock, then use click, W/L/D and modules 1–9.", "先与代理人交谈，离站后使用点击、W/L/D 键与 1–9 号模块。"),
            ("Help: click to select · double-click approach · W warp · L lock · D dock/jump · V/X focus · 1–9 modules.", "帮助：单击选择 · 双击接近 · W 跃迁 · L 锁定 · D 停靠/跳跃 · V/X 聚焦 · 1–9 模块。"),
            ("Context: Approach · Orbit · Warp · Lock · Dock/Jump.", "情境菜单：接近 · 环绕 · 跃迁 · 锁定 · 停靠/跳跃。"),
            ("No valid save slot was found.", "未找到有效的存档位。"),
            ("Place a legacy JSON save in Downloads or as persistentDataPath/legacy-v1.json, then try again.", "请将旧版 JSON 存档放入“下载”目录，或保存为 persistentDataPath/legacy-v1.json 后重试。"),
            ("Legacy import rejected: {0}", "旧版存档导入被拒绝：{0}"),
            ("Legacy v1 imported to slot1. The source file was not modified.", "旧版 V1 存档已导入存档位1。源文件未被修改。"),
            ("Legacy import failed: {0}", "旧版存档导入失败：{0}"),
            ("Quality preset: {0}.", "画质预设：{0}。"),
            ("Game saved to {0}.", "游戏已保存至{0}。"),
            ("Save failed: {0}.", "存档失败：{0}。"),
            ("Save loaded.", "存档已加载。"),
            ("auto", "自动存档"),
            ("slot1", "存档位1"),
            ("slot2", "存档位2"),
            ("slot3", "存档位3"),

            // --- AppRoot: starmap / journal / pilot logs ---
            ("Starmap open · Adjacent: {0}", "星图已打开 · 相邻：{0}"),
            ("Starmap closed · {0}", "星图已关闭 · 相邻：{0}"),
            ("Journal: no active missions.", "日志：暂无进行中的任务。"),
            ("Journal: {0}", "日志：{0}"),
            ("Pilot {0} · {1} kills · {2} missions · {3} ore · {4} jumps.", "飞行员 {0} · 击杀 {1} · 任务 {2} · 矿石 {3} · 跳跃 {4}。"),
            ("Command target", "指令目标"),
            ("Docked", "已停靠"),

            // --- AppRoot: list rows (starmap, market, fitting, ships) ---
            ("NO ROUTE", "无航线"),
            ("{0} jumps", "{0} 跳"),
            (" · DESTINATION", " · 目的地"),
            (" · SEC {0}", " · 安等 {0}"),
            ("{0} ISK base", "{0} ISK 基准价"),
            ("SELL CARGO · {0} ×{1}", "出售货物 · {0} ×{1}"),
            ("Sell the full stack · {0} per unit base", "整组出售 · 单位基准价 {0}"),
            ("SELL HANGAR · {0} ×{1}", "出售机库物资 · {0} ×{1}"),
            ("Sell one module · {0}", "出售一件模块 · {0}"),
            ("ACTIVE · {0}", "现役 · {0}"),
            ("HANGAR · {0} ×{1}", "机库 · {0} ×{1}"),
            ("Click to fit the first compatible free slot", "点击装配到第一个兼容的空槽位"),
            ("FITTED · {0} {1} · {2}", "已装配 · {0} {1} · {2}"),
            ("Click to unfit this module to the hangar", "点击将此模块卸回机库"),
            ("No compatible free slot.", "没有兼容的空槽位。"),

            // --- OverviewContactBuilder ---
            ("Station", "空间站"),
            ("Dock within 40 m", "40 米内可停靠"),
            ("Stargate", "星门"),
            ("Jump to {0}", "跳跃至{0}"),
            ("Asteroid Belt", "小行星带"),
            ("{0} units remaining", "剩余 {0} 单位"),
            ("{0}-class Star", "{0}级恒星"),
            ("System primary", "星系主星"),
            ("Planet", "行星"),
            ("Moon", "卫星"),
            ("Natural satellite", "天然卫星"),
        };
    }
}
