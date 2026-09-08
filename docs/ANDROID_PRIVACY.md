# Android privacy gate

TapTap's Android 10 report for release `1.0.0-internal.100.1` identified Unity sensor-list access before disclosure/consent. A Unity UI dialog is too late to guard engine initialization.

## Implementation

- The existing `com.pzy.starfall.mobile.StarfallUnityGameActivity` component name is retained as a **native consent launcher**, now extending `PrivacyConsentActivity`, not Unity. This also routes launches retained from the prior APK through consent after an upgrade.
- The launcher runs in the `:privacy` process. The actual Unity implementation moved unchanged to `StarfallUnityPlayerActivity` in the main process. That activity is not exported and has no launcher filter.
- The native gate has no Unity class reference, web view, sensor call or network call. It reads a bundled policy text before rendering and fails closed if the policy is missing or its version does not match.
- Only an explicit “同意并进入” tap saves the policy version with Android `AtomicFile`, reads it back, and starts the protected player. “不同意并退出” / Back closes the gate without consent. Opening the full offline policy does not accept it.
- Consent is in `getNoBackupFilesDir()`, not transferable through standard backup. A stale policy token or cleared application data requires acceptance again. When policy processing changes, update the token and bundled document together. Restoring an already-running Unity task across a future policy-changing upgrade requires additional lifecycle verification; the current guard targets the existing APK's first consent rollout.
- Current policy: https://pzy2000.github.io/EVE_remake/privacy/ ; privacy contact: pzy2000@sjtu.edu.cn. The bundled document is a readable copy of that policy as of 2026-09-07.

## Tests

Fast source/export checks:

```sh
python3 scripts/android-privacy-policy-test.py
bash scripts/android-java-policy-test.sh
python3 scripts/android-build-policy-test.py
```

Android native UI integration tests (JDK 17, Android SDK platform 36 + build-tools 36.0.0, a running emulator):

```sh
export ANDROID_SDK_ROOT=/absolute/path/to/android-sdk
bash scripts/build-android-privacy-fixture.sh artifacts/privacy-test
bash scripts/build-android-privacy-fixture.sh artifacts/privacy-test-legacy legacy
python3 scripts/test-android-privacy-ui.py \
  --apk artifacts/privacy-test/privacy-test.apk \
  --legacy-apk artifacts/privacy-test-legacy/privacy-test.apk \
  --output artifacts/privacy-test/results
```

These APKs compile the production gate and launcher but use a clearly labelled **player test double**. They have a separate package (`com.pzy.starfall.privacytest`) and test signing key. They must never be published as the game. Tests cover install upgrade, first launch, rejection, Back, offline policy, external intent denial, explicit acceptance, relaunch, stale consent, persistence failure and clear-data behavior. Screenshots and JSON results are retained in the output directory.

The existing game emulator CI scripts now interact with the consent UI before waiting for Unity. Release validation checks the merged manifest's isolated launcher and non-exported player. There is no production consent bypass for CI.

## Remaining release validation

The existing published APK is unchanged. The local tests do **not** prove that a newly built Unity APK passes TapTap's sensor/privacy detection. Build a new signed APK/AAB from the source, run the full Unity/Android CI matrix and verify on Android 10 (the report environment) that no Unity/native player initialization or sensor-list call occurs before acceptance. Re-run TapTap privacy testing on the new package before claiming the report fixed. The local machine currently lacks the Unity 6000.1.10f1 Android build module, so the complete signed Unity build was not produced in this change.
