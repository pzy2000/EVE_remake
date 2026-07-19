package com.pzy.starfall.mobile;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;

/** Plain-JVM regression test; no Android runtime or JUnit dependency is required. */
@SuppressWarnings("auxiliaryclass")
public final class StarfallMobileBridgeLifecycleTest {
    private StarfallMobileBridgeLifecycleTest() {
    }

    public static void main(String[] arguments) throws IOException {
        BridgeLifecycleGeneration lifecycle = new BridgeLifecycleGeneration();
        int pendingInitialize = lifecycle.next();
        assertTrue(lifecycle.isCurrent(pendingInitialize),
                "the latest initialize request must remain current");

        int shutdown = lifecycle.next();
        assertFalse(lifecycle.isCurrent(pendingInitialize),
                "shutdown must invalidate a queued initialize request");
        assertTrue(lifecycle.isCurrent(shutdown),
                "shutdown must own the new lifecycle generation");

        int reinitialize = lifecycle.next();
        assertFalse(lifecycle.isCurrent(shutdown),
                "a newer initialize request must invalidate queued shutdown cleanup");
        assertTrue(lifecycle.isCurrent(reinitialize),
                "the newest initialize request must be the only current generation");

        assertFalse(CompatBackPolicy.shouldHandleWithActivityCallback(25),
                "SDKs below the application minimum must not use the compatibility callback");
        assertTrue(CompatBackPolicy.shouldHandleWithActivityCallback(26),
                "API 26 must use the Activity compatibility callback");
        assertTrue(CompatBackPolicy.shouldHandleWithActivityCallback(32),
                "API 32 must use the Activity compatibility callback");
        assertFalse(CompatBackPolicy.shouldHandleWithActivityCallback(33),
                "API 33 must remain on the predictive back callback");
        assertFalse(CompatBackPolicy.shouldHandleWithActivityCallback(36),
                "API 36 must remain on the predictive back callback");
        assertFalse(CompatBackPolicy.shouldHandleKeyEvent(25),
                "SDKs below the application minimum must not consume hardware Back");
        assertTrue(CompatBackPolicy.shouldHandleKeyEvent(26),
                "API 26 must consume hardware Back through GameActivity");
        assertTrue(CompatBackPolicy.shouldHandleKeyEvent(32),
                "API 32 must consume hardware Back through GameActivity");
        assertFalse(CompatBackPolicy.shouldHandleKeyEvent(33),
                "API 33 Back keys must not duplicate the predictive callback");
        assertFalse(CompatBackPolicy.shouldHandleKeyEvent(36),
                "API 36 ADB Back must dispatch only through the predictive callback");

        if (arguments.length >= 1) {
            verifyProductionWiring(Paths.get(arguments[0]));
        }
        if (arguments.length == 7) {
            verifyCustomActivityWiring(
                    Paths.get(arguments[1]), Paths.get(arguments[2]), Paths.get(arguments[3]));
            verifyLegacyImportWiring(Paths.get(arguments[4]), Paths.get(arguments[5]));
            verifyActivityPluginImporter(Paths.get(arguments[6]));
        } else if (arguments.length != 0 && arguments.length != 1) {
            throw new IllegalArgumentException(
                    "Expected no arguments, bridge source only, or all production source paths");
        }
    }

    private static void verifyProductionWiring(Path bridgeSource) throws IOException {
        String source = new String(Files.readAllBytes(bridgeSource), StandardCharsets.UTF_8);
        assertContains(source, "startWindowTrackingLocked(requestGeneration)");
        assertContains(source, "registerBackCallbackLocked(requestGeneration)");
        assertContains(source, "registerDebugReceiverLocked(requestGeneration)");
        assertContains(source, "if (!isApplicationDebuggable(activity))");
        assertContains(source, "ApplicationInfo.FLAG_DEBUGGABLE");
        assertFalse(source.contains("if (!BuildConfig.DEBUG || activity == null)"),
                "debug receiver is still gated by the Android library build variant");
        assertContains(source,
                "dispatchDebugCommand(intent, requestGeneration, registeredActivity)");
        assertContains(source, "if (!LIFECYCLE.isCurrent(requestGeneration))");
        assertContains(source, "case \"select-first-hostile\":");
        assertContains(source, "case \"lock-selected\":");
        assertContains(source, "case \"show-death-overlay\":");
        assertContains(source, "case \"seed-low-memory-fixture\":");
        assertContains(source, "static boolean dispatchCompatBack(Activity sourceActivity)");
        assertContains(source, "if (!initialized || activity != sourceActivity)");
        assertContains(source, "long sequence = dispatchBackLocked()");
        assertContains(source, "sendUnity(CALLBACK_BACK, Long.toString(sequence))");
        assertContains(source, "dispatchBackIfLifecycleActive(");
        assertContains(source, "STARFALL_ANDROID_BACK_COMPAT_DISPATCH=");
        assertContains(source, "STARFALL_ANDROID_BACK_PREDICTIVE_DISPATCH=");
        assertContains(source, "OnBackInvokedDispatcher.PRIORITY_OVERLAY");
        assertFalse(source.contains("OnBackInvokedDispatcher.PRIORITY_DEFAULT"),
                "predictive Back must not lose to Unity GameActivity's callback priority");
        assertContains(source, "drainReadyLegacyImportsLocked(currentActivity)");
        assertContains(source, "public static void acknowledgeLegacyDocument");
        assertContains(source, "consumeRestoredLegacyImportAcknowledgement");
    }

    private static void verifyCustomActivityWiring(
            Path activitySource, Path manifestSource, Path projectSettings) throws IOException {
        String activity = new String(Files.readAllBytes(activitySource), StandardCharsets.UTF_8);
        assertContains(activity, "extends UnityPlayerGameActivity");
        assertContains(activity,
                "CompatBackPolicy.shouldHandleKeyEvent(Build.VERSION.SDK_INT)");
        assertContains(activity,
                "CompatBackPolicy.shouldHandleWithActivityCallback(Build.VERSION.SDK_INT)");
        assertContains(activity, "public boolean onKeyDown(int keyCode, KeyEvent event)");
        assertContains(activity, "return super.onKeyDown(keyCode, event)");
        assertContains(activity, "public boolean onKeyUp(int keyCode, KeyEvent event)");
        assertContains(activity, "!event.isCanceled()");
        assertContains(activity, "return super.onKeyUp(keyCode, event)");
        assertContains(activity, "public boolean dispatchKeyEvent(KeyEvent event)");
        assertContains(activity, "event.getKeyCode() == KeyEvent.KEYCODE_BACK");
        assertContains(activity, "event.getAction() == KeyEvent.ACTION_UP");
        assertContains(activity, "event.getRepeatCount() == 0");
        assertContains(activity, "StarfallMobileBridge.dispatchCompatBack(this)");
        assertContains(activity, "return super.dispatchKeyEvent(event)");
        assertContains(activity, "super.onBackPressed()");
        assertBefore(activity, "StarfallMobileBridge.dispatchCompatBack(this)", "super.onBackPressed()");

        String manifest = new String(Files.readAllBytes(manifestSource), StandardCharsets.UTF_8);
        assertContains(manifest,
                "android:name=\"com.pzy.starfall.mobile.StarfallUnityGameActivity\"");
        assertContains(manifest, "android:exported=\"true\"");
        assertContains(manifest, "android:resizeableActivity=\"true\"");
        assertContains(manifest, "android:screenOrientation=\"sensorLandscape\"");
        assertContains(manifest, "android:name=\"android.intent.action.MAIN\"");
        assertContains(manifest, "android:name=\"android.intent.category.LAUNCHER\"");
        assertContains(manifest, "android:name=\"android.app.lib_name\"");
        assertContains(manifest, "android:value=\"game\"");

        String settings = new String(Files.readAllBytes(projectSettings), StandardCharsets.UTF_8);
        assertContains(settings, "useCustomMainManifest: 1");
    }

    private static void verifyLegacyImportWiring(
            Path pickerSource, Path libraryManifestSource) throws IOException {
        String picker = new String(Files.readAllBytes(pickerSource), StandardCharsets.UTF_8);
        assertContains(picker, "STATE_RESULT_CONSUMED");
        assertContains(picker, "STATE_COPY_STARTED");
        assertContains(picker, "STATE_SOURCE_URI");
        assertContains(picker, "startCopy(Uri.parse(sourceUriString))");
        assertContains(picker, "private static ImportWork activeWork");
        assertContains(picker, "activeWork.matches(sourceUri.toString(), unityGameObject)");
        assertContains(picker, "File.createTempFile(\"legacy-v1-\", \".part\"");
        assertContains(picker, "intent.setType(\"*/*\")");
        assertContains(picker, "Intent.EXTRA_MIME_TYPES");
        assertContains(picker, "new String[]{\"application/json\", \"text/json\"}");
        assertContains(picker, "writeReadyMarker(readyMarker)");
        assertContains(picker, "partial.renameTo(destination)");
        assertContains(picker, "StarfallMobileBridge.notifyLegacyDocumentReady");
        assertBefore(picker, "validateJsonObject(partial)", "partial.renameTo(destination)");
        assertBefore(picker, "writeReadyMarker(readyMarker)", "partial.renameTo(destination)");

        String manifest = new String(
                Files.readAllBytes(libraryManifestSource), StandardCharsets.UTF_8);
        assertContains(manifest,
                "android:name=\"com.pzy.starfall.mobile.LegacyDocumentPickerActivity\"");
        assertContains(manifest, "android:configChanges=");
    }

    private static void verifyActivityPluginImporter(Path metaSource) throws IOException {
        String meta = new String(Files.readAllBytes(metaSource), StandardCharsets.UTF_8);
        assertContains(meta, "PluginImporter:");
        assertContains(meta, "Android: Android");
        assertContains(meta, "enabled: 1");
    }

    private static void assertContains(String source, String expected) {
        assertTrue(source.contains(expected), "production bridge is missing lifecycle gate: " + expected);
    }

    private static void assertBefore(String source, String first, String second) {
        int firstIndex = source.indexOf(first);
        int secondIndex = source.indexOf(second);
        assertTrue(firstIndex >= 0 && secondIndex > firstIndex,
                "expected production ordering: " + first + " before " + second);
    }

    private static void assertTrue(boolean value, String message) {
        if (!value) {
            throw new AssertionError(message);
        }
    }

    private static void assertFalse(boolean value, String message) {
        assertTrue(!value, message);
    }
}
