# STARFALL ODYSSEY Android CI

The Android pipeline is implemented by `.github/workflows/android.yml`. It does not require Android Studio: Unity exports a Gradle project, then the workflow uses JDK 17 and the Android command-line SDK to produce the signed APK and AAB.

## Fixed release contract

- Unity: `6000.1.10f1`.
- Package: `com.pzy.starfallodyssey`.
- Android: minimum API 26, target API 36, landscape left/right, resizable GameActivity.
- Release ABI/backend: `arm64-v8a` and IL2CPP.
- Graphics API order: Vulkan, then OpenGL ES 3.
- Version code: `github.run_number * 100 + github.run_attempt`.
- Version name: `1.0.0-internal.<run_number>.<run_attempt>`.
- Release outputs: signed APK, signed AAB, IL2CPP symbols, JSON BuildReport, validation evidence, and `SHA256SUMS`.
- Every successful trusted branch push creates a uniquely versioned GitHub pre-release tagged `android-v<versionName>` and attaches the signed APK plus its companion evidence. Broken or unverified pushes never publish an APK.

The release validator checks package/version/API metadata, non-debuggable state, ARM64-only libraries, matching APK/AAB certificates, adaptive launcher resources, `bundletool validate`, and 16 KiB ZIP/ELF alignment. It also builds a signed bundletool universal APK and repeats the ARM64-only and 16 KiB checks on that Play-style artifact. Release IL2CPP output is rejected if the smoke-only command callback is present.

Unity discovery is also an exact hard gate: EditMode must report 144 cases (109 desktop baseline plus 35 Android/mobile cases), and PlayMode must report 24 cases (19 desktop baseline plus 5 Android/mobile cases). A passing subset or silently undiscovered fixture cannot satisfy CI.

## GitHub configuration

Add these repository secrets for Unity testing and signed GitHub Releases:

- `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`
- `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`
- `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASSWORD`

Google Play publication is intentionally independent from GitHub Releases. When Play Console approval is complete, add `GOOGLE_PLAY_SERVICE_ACCOUNT_JSON`, create a protected GitHub Environment named `google-play-internal`, and set the repository variable `ENABLE_PLAY_INTERNAL=true`. Until that variable is enabled, the Play job is skipped without blocking signed APK publication. The Play service account should have only the permissions needed to publish the application to Internal Testing. Signing material is decoded beneath `RUNNER_TEMP`, never passed in GameCI custom parameters, and removed by an EXIT trap.

Before the first automatic upload, create `com.pzy.starfallodyssey` in Play Console, enable Play App Signing, link the service account, and finish the Play Console setup needed to permit a `completed` Internal Testing release. Fastlane does not upload or modify store metadata, screenshots, images, or changelogs.

Fork pull requests run only the Node reference suite and static CI checks. Unity licensing, signing, and Play credentials are never exposed to fork jobs. The workflow intentionally has no `pull_request_target` trigger.

Every push and pull request runs the reference suite. Trusted pushes and same-repository pull requests enter Unity only after a named-secret preflight; missing configuration fails explicitly without printing secret values. Every successful trusted push builds and validates the signed ARM64 outputs and publishes a GitHub pre-release. Play Internal Testing remains restricted to successful `main` pushes and the explicit `ENABLE_PLAY_INTERNAL=true` opt-in.

## Emulator architecture

The primary smoke gate is fixed to API 36 `x86_64` on `ubuntu-24.04`; it is explicitly a test-only build carrying `STARFALL_ANDROID_CI`. Both emulator jobs use the lighter AOSP `default` image because the game and Android Storage Access Framework do not require Google Play Services; this also preserves guest memory for the high-resolution SwiftShader framebuffer. Each emulator uses a bounded 4096 MiB runner allocation and the supported `-gpu swiftshader` software backend; `swiftshader_indirect` was deprecated by Android Emulator 36.4.9. Larger reservations can make the hosted-runner emulator process disappear while SwiftShader reallocates the exact `2748x1172` framebuffer. A separate short API 32 `x86_64` job reuses that APK at `1600x900 @ 320dpi` and proves the API 26-32 compatibility path with a real `KEYCODE_BACK`: MainMenu must show and then close its confirmation while the same app process stays alive and its logs remain free of fatal/ANR signals. Both API 36 acceptance and API 32 Back compatibility are required before any signed GitHub Release. Release APK/AAB outputs are always ARM64 IL2CPP. Unity does not support Android Emulator as a production hardware target, so these gates do not claim real-device GPU, thermal, throttling, or frame-rate coverage.

The emulator gate applies exact `wm size` and density overrides for:

- `2748x1172 @ 420dpi` (`CompactLandscape`)
- `2480x2200 @ 420dpi` (`SquareExpanded`)
- `2480x2200 @ 420dpi` with an 84px vertical occluding hinge
- `2480x2200 @ 420dpi` with an 80px horizontal `HALF_OPENED` fold
- quick 320dpi and 560dpi geometry stress cases
- Vulkan-preferred startup on both primary sizes and an explicit OpenGL ES 3 startup at `2748x1172`

The test-only debuggable receiver accepts deterministic metrics at action `com.pzy.starfall.mobile.DEBUG_WINDOW_LAYOUT` with JSON extra key `json`. Unity script debugging remains disabled to avoid unnecessary JDWP/adbd memory pressure. The script reads `starfall-ci-layout*.json` from Unity's external `Application.persistentDataPath` through the ADB shell and rejects wrong layout modes, raw interactive targets smaller than 48dp, visible text smaller than 14dp, clipped visible content outside the safe pane, and complete panels crossing a hinge. MainMenu, Station, Space, Settings, Starmap, Journal, Combat Log, Back confirmation, and the explicitly labelled smoke-only death fixture each retain original-size screenshots and layout JSON. MainMenu, Station, and Space also exercise the real `KEYCODE_BACK` confirmation/close order; Settings and the remaining overlays must close before gameplay navigation.

Every graphics scenario records Unity's runtime `SystemInfo.graphicsDeviceType` and `graphicsDeviceName`; the requested command-line flag alone is not accepted as runtime proof. The signed release BuildReport hard-fails unless its configured API order is exactly `Vulkan,OpenGLES3`. Unity may reject the Android Emulator's CPU-backed SwiftShader Vulkan device even after `-force-vulkan`; only when `ro.kernel.qemu=1` and Unity reports the exact Android Emulator/SwiftShader GLES3 device does CI accept and explicitly label `verified-emulator-vulkan-fallback`. Any other Vulkan mismatch fails, as does a forced GLES scenario that does not report `OpenGLES3`. This is fallback evidence, not a claim of physical-device Vulkan validation. Before installation and cold starts, CI may act only on an error dialog whose focused window/text proves that it belongs to System UI or the configured Android Launcher. The window dump, UI hierarchy, and screenshot are retained. An app-owned or unknown error dialog is never dismissed and fails the job.

The app's Import button is used to enter `LegacyDocumentPickerActivity`; ADB then selects a real JSON document through DocumentsUI. CI validates Slot 1, the converted player's name/source SHA, the unchanged provider file, and an empty import cache. A separate smoke-only fixed command channel runs 20 Station/Space cycles, 12 actual jumps, a hostile warp/lock/module engagement, and the median PSS rule after three warm-up cycles. Ten pause/focus cycles must rewrite a valid auto save, the API 36 `RUNNING_CRITICAL` trim callback is a hard gate with seeded-cache release/process/memory/log evidence, and force-stop recovery must return through MainMenu's Continue button to the saved Space session. The release library has no exported debug receiver, `BuildConfig.DEBUG` disables dynamic debug registration, and the C# command callback is absent from release IL2CPP.

## Reproducibility and security

All GitHub Actions use full commit SHAs. Both GameCI actions use the reviewed Unity Android image by full multi-architecture digest:

`unityci/editor:6000.1.10f1-android-3@sha256:ed46b4a1ad2b6c30d752bf2c988b00038ab84551a07625875a34d0d81be71c42`

Unity Personal jobs share a repository-wide concurrency lock. Runner cleanup is followed by a hard 25 GiB free-space check. The custom build entry validates generated visual assets without rewriting them, snapshots/restores PlayerSettings, and CI fails if tracked or untracked source changes exist before or after Unity.

Fastlane is fixed to `2.237.0`; `Gemfile.lock` pins its transitive graph and Bundler `2.4.22`, while `vendor/bundle` remains ignored.

## Local and remote verification

Run the non-Unity checks locally:

```bash
npm test
bash scripts/android-java-policy-test.sh
ruby scripts/validate-workflow-yaml.rb .github/workflows/android.yml
ruby -c fastlane/Fastfile
bash -n scripts/android-*.sh scripts/validate-android-release.sh
git diff --check
```

After pushing, wait for the remote gate and retrieve its evidence:

```bash
gh run list --workflow android.yml --limit 5
gh run watch <run-id> --exit-status
gh run download <run-id> --dir artifacts/remote-android
```

GitHub Release completion requires the corresponding `android-v<versionName>` pre-release to contain the signed APK, AAB, IL2CPP symbols, BuildReport, checksums, and validation archive. Google Play delivery remains a separate completion gate once `ENABLE_PLAY_INTERNAL=true`. Emulator results cover CI functionality, layout, lifecycle, logs, and package structure; they are not evidence of real ARM64 GPU performance, thermals, throttling, or physical-device frame rate.
