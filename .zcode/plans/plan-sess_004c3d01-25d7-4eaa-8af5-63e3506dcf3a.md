# 通过 taptap-cli 更新线上版本并提审

## 已确认的事实
- `taptap-cli`（@taptap/cli 官方 CLI）已装于 `/opt/homebrew/bin/taptap-cli`，随包附带了完整的业务 skill 文档（identity / package-management / app-edit 等），流程以文档为准。
- 新包已构建：`build/android/app.apk`（约 88.9MB，今日构建，Unity 6000.1.10f1 IL2CPP，debug 签名），包名 `com.UnityTechnologies.com.unity.template.urpblank`，工程当前 versionCode 1 / versionName 1.0.0。
- 游戏：STARFALL ODYSSEY。本次更新内容（据近期提交）：折叠屏 UI 缩放适配、多语言本地化、音频设置、太空视觉与遥测优化。
- CLI 硬性门禁：提审意图必须由用户明确提出（本次"提交审核"已满足）；提审链路固定为 `prepare-review-snapshot` → `precheck-app-review` → `submit-app-review --yes`，同一个 `review_fingerprint` 和 `release_schedule` 贯穿三步；提审前须用 `list-app-versions --page-all` 判定运营阶段（已有上线历史 → 长线）。

## 执行步骤

1. **登录与身份定位**
   - `taptap-cli auth status`；未登录则 `auth login --no-wait --json`，将授权链接单独一行展示给你，并后台轮询 device code 完成登录。
   - `taptap-cli overview` / `app +list --kw "STARFALL"` 定位 developerId、appId；多候选时请你确认目标，并展示服务端返回的状态标签（预期"已上线"）。

2. **读线上包体与版本历史（read-before-write）**
   - `package-management get-package-overview`：读线上 APK 的 versionCode / versionName。
   - `app list-app-versions --page-all`：确认当前版本状态与完整发布历史。

3. **版本号门禁（如需则重建）**
   - 若现有 APK 的 versionCode（1）不高于线上包：将 `UnityProject/ProjectSettings` 的 `AndroidBundleVersionCode` 提到线上值 +1（默认 2）、`bundleVersion` 改为 1.1.0，按 unity-android-build 流程重新构建出 `app.apk`；否则直接用现有包。

4. **上传 APK（先 dry-run 再 --yes）**
   - `taptap-cli upload-apk build/android/app.apk --dev-id <id> --app-id <id> --dry-run` 预览通过后加 `--yes` 上传。
   - 上传成功 ≠ 主包体已绑定；上传后重新读取包体概览确认新包 `ready`。

5. **绑定主包体（app-edit 流程）**
   - 若游戏已上线且无未发布草稿，按文档 `create-draft` 基于线上版本开新草稿。
   - `app list-packages`：确认 `package_slots.main.available=true` 且新包 `status=ready`，原样取本次 `expected`；`select-package` 带 `expected` + 稳定幂等键执行，成功后读回验证绑定。

6. **版本更新说明与资料体检**
   - 若更新说明字段可见/必填：按近期提交写更新说明（折叠屏 UI 适配、多语言本地化、音频设置、视觉优化），read-before-write 用 `save-changes` 写入（每批 ≤5 字段）。
   - 顺带读取资料模块，确认没有新增提审 blocker。

7. **提审（三步，同指纹同档期 immediate）**
   - `prepare-review-snapshot`（read）→ 保存 `review_fingerprint`。
   - `precheck-app-review`（read，同指纹同档期）→ 若返回 `required_consents`：只展示协议名称与 URL，征得你明确同意后才携带 `consent_tokens` 提交。
   - 预检无阻塞项后执行 `submit-app-review --yes`（同指纹、同档期、稳定幂等键）。**若预检发现资质/认证/素材等无法自动补齐的 blocker，停止并列出缺口，不提交。**
   - 成功后 `list-app-versions` 读回，确认进入"审核中"，并输出 handoff（当前阶段、等待事项、下一步检查方式）。

## 风险与说明
- APK 为 debug 签名（本机构建管线默认）；TapTap 审核一般接受，若上传校验拒绝再另行处理。
- 需要你参与的点：登录授权（若未登录）、多候选游戏确认、预检协议同意（如出现）。其余步骤自动推进。
- 全程不输出凭证/敏感信息；写操作均带幂等键，收到 stale/409 时重读状态而非盲目重试。