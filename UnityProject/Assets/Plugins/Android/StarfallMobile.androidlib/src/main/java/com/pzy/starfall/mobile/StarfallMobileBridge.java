package com.pzy.starfall.mobile;

import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.ApplicationInfo;
import android.graphics.Rect;
import android.os.Build;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.DisplayCutout;
import android.view.View;
import android.view.WindowInsets;

import androidx.core.util.Consumer;
import androidx.window.java.layout.WindowInfoTrackerCallbackAdapter;
import androidx.window.layout.DisplayFeature;
import androidx.window.layout.FoldingFeature;
import androidx.window.layout.WindowInfoTracker;
import androidx.window.layout.WindowLayoutInfo;
import androidx.window.layout.WindowMetricsCalculator;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.IOException;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.Executor;

/**
 * JNI-friendly entry point for STARFALL ODYSSEY's Android integration.
 *
 * C# calls the public static methods through AndroidJavaClass. Native events are
 * delivered to the Unity GameObject supplied to {@link #initialize(String)}.
 */
public final class StarfallMobileBridge {
    private static final String TAG = "StarfallMobile";
    private static final String DEFAULT_UNITY_GAME_OBJECT = "StarfallApp";
    private static final long STALE_IMPORT_MILLIS = 5L * 60L * 1000L;

    public static final String DEBUG_WINDOW_ACTION =
            "com.pzy.starfall.mobile.DEBUG_WINDOW_LAYOUT";
    public static final String DEBUG_COMMAND_ACTION =
            "com.pzy.starfall.mobile.DEBUG_COMMAND";

    private static final String CALLBACK_WINDOW_LAYOUT = "OnAndroidWindowLayoutInfo";
    private static final String CALLBACK_CI_COMMAND = "OnAndroidCiCommand";
    private static final String CALLBACK_BACK = "OnAndroidBackInvoked";
    static final String CALLBACK_DOCUMENT_PICKED = "OnAndroidLegacyDocumentPicked";
    static final String CALLBACK_DOCUMENT_ERROR = "OnAndroidLegacyDocumentPickerError";

    private static final Object LOCK = new Object();
    private static final BridgeLifecycleGeneration LIFECYCLE =
            new BridgeLifecycleGeneration();
    private static volatile String unityGameObject = DEFAULT_UNITY_GAME_OBJECT;
    private static volatile String latestRealWindowJson = emptyWindowJson("unavailable");
    private static volatile String latestWindowJson = latestRealWindowJson;
    private static volatile String debugWindowJson;

    private static Activity activity;
    private static WindowInfoTrackerCallbackAdapter windowTracker;
    private static Consumer<WindowLayoutInfo> windowConsumer;
    private static Executor windowExecutor;
    private static BroadcastReceiver debugReceiver;
    private static Object backCallback;
    private static boolean initialized;
    private static boolean legacyImportCacheCleaned;
    private static boolean restoredLegacyImportAcknowledged;
    private static long backDispatchSequence;
    private static String pendingLegacyErrorTarget;
    private static String pendingLegacyErrorPayload;
    private static final Set<String> dispatchedLegacyImports = new HashSet<>();

    private StarfallMobileBridge() {
    }

    /** Starts window, fold, and Android 13+ back observation. Safe to call repeatedly. */
    public static void initialize(String targetGameObject) {
        final int requestGeneration;
        synchronized (LOCK) {
            unityGameObject = normalizeUnityGameObject(targetGameObject);
            requestGeneration = LIFECYCLE.next();
            // Close the old callback gate immediately; listener cleanup must wait for UI.
            initialized = false;
        }
        final Activity currentActivity = findUnityActivity();
        if (currentActivity == null) {
            synchronized (LOCK) {
                if (LIFECYCLE.isCurrent(requestGeneration)) {
                    latestRealWindowJson = emptyWindowJson("unity-activity-unavailable");
                    latestWindowJson = latestRealWindowJson;
                }
            }
            Log.e(TAG, "Unity activity is unavailable; native bridge was not initialized.");
            return;
        }

        cleanupLegacyImportCacheOnce(currentActivity);

        currentActivity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                synchronized (LOCK) {
                    if (!LIFECYCLE.isCurrent(requestGeneration)) {
                        return;
                    }
                    stopLocked();
                    activity = currentActivity;
                    initialized = true;
                    startWindowTrackingLocked(requestGeneration);
                    registerBackCallbackLocked(requestGeneration);
                    registerDebugReceiverLocked(requestGeneration);
                    boolean resumedImport = drainReadyLegacyImportsLocked(currentActivity);
                    if (!resumedImport && pendingLegacyErrorPayload != null) {
                        sendUnityTo(
                                pendingLegacyErrorTarget,
                                CALLBACK_DOCUMENT_ERROR,
                                pendingLegacyErrorPayload);
                    }
                    pendingLegacyErrorTarget = null;
                    pendingLegacyErrorPayload = null;
                }
            }
        });
    }

    /** Removes all native listeners. The document picker, if open, finishes independently. */
    public static void shutdown() {
        final Activity currentActivity;
        final int requestGeneration;
        synchronized (LOCK) {
            requestGeneration = LIFECYCLE.next();
            // Reject callbacks synchronously, even though Android listener removal is asynchronous.
            initialized = false;
            currentActivity = activity;
        }
        if (currentActivity == null) {
            synchronized (LOCK) {
                if (LIFECYCLE.isCurrent(requestGeneration)) {
                    stopLocked();
                }
            }
            return;
        }

        currentActivity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                synchronized (LOCK) {
                    if (LIFECYCLE.isCurrent(requestGeneration)) {
                        stopLocked();
                    }
                }
            }
        });
    }

    /** Returns the most recently dispatched schema-v1 window JSON. */
    public static String getLatestWindowLayoutInfoJson() {
        return latestWindowJson;
    }

    /**
     * Returns the same app-private cache root used by LegacyDocumentPickerActivity.
     * C# queries this independently so the callback path is never treated as its own
     * trust anchor (Unity's temporaryCachePath can point at a different cache volume).
     */
    public static String getLegacyImportCacheRoot() {
        Activity currentActivity = findUnityActivity();
        if (currentActivity == null) {
            throw new IllegalStateException("Unity activity is unavailable");
        }
        File cacheDirectory = currentActivity.getCacheDir();
        if (cacheDirectory == null) {
            throw new IllegalStateException("Android app cache directory is unavailable");
        }
        return cacheDirectory.getAbsolutePath();
    }

    /** Removes only incomplete/orphaned files; validated .ready/.json pairs are resumable. */
    private static void cleanupLegacyImportCacheOnce(Activity currentActivity) {
        synchronized (LOCK) {
            if (legacyImportCacheCleaned) {
                return;
            }
            legacyImportCacheCleaned = true;
        }

        File cacheDirectory = currentActivity.getCacheDir();
        if (cacheDirectory == null) {
            return;
        }
        File importDirectory = new File(cacheDirectory, "legacy-import");
        File[] candidates = importDirectory.listFiles();
        if (candidates == null) {
            return;
        }
        long staleBefore = System.currentTimeMillis() - STALE_IMPORT_MILLIS;
        for (File candidate : candidates) {
            String name = candidate.getName();
            if (!candidate.isFile() || !name.startsWith("legacy-v1-")) {
                continue;
            }
            boolean abandoned = name.endsWith(".part");
            if (name.endsWith(".ready")) {
                abandoned = !legacyJsonForReadyMarker(candidate).isFile();
            } else if (name.endsWith(".json")) {
                abandoned = !legacyReadyMarkerForJson(candidate).isFile();
            }
            if (abandoned && candidate.lastModified() < staleBefore && !candidate.delete()) {
                Log.w(TAG, "Unable to remove abandoned legacy import file: " + name);
            }
        }
    }

    static void notifyLegacyDocumentReady(String targetGameObject, String absoluteCachePath) {
        final String canonicalPath;
        final boolean shouldDispatch;
        synchronized (LOCK) {
            if (!initialized || activity == null) {
                // The durable .ready/.json pair is drained after the Unity bridge initializes.
                return;
            }
            canonicalPath = validateLegacyImportPath(activity, absoluteCachePath, true);
            shouldDispatch = dispatchedLegacyImports.add(canonicalPath);
        }
        if (shouldDispatch) {
            sendUnityTo(targetGameObject, CALLBACK_DOCUMENT_PICKED, canonicalPath);
        }
    }

    static void notifyLegacyDocumentError(String targetGameObject, String payload) {
        final boolean shouldDispatch;
        synchronized (LOCK) {
            shouldDispatch = initialized && activity != null;
            if (!shouldDispatch) {
                pendingLegacyErrorTarget = targetGameObject;
                pendingLegacyErrorPayload = payload;
            }
        }
        if (shouldDispatch) {
            sendUnityTo(targetGameObject, CALLBACK_DOCUMENT_ERROR, payload);
        }
    }

    /** Called by C# after it has consumed/deleted the validated cache JSON. */
    public static void acknowledgeLegacyDocument(String absoluteCachePath) {
        Activity currentActivity = findUnityActivity();
        String canonicalPath = validateLegacyImportPath(
                currentActivity, absoluteCachePath, false);
        File marker = legacyReadyMarkerForJson(new File(canonicalPath));
        if (marker.exists() && !marker.delete()) {
            Log.w(TAG, "Unable to delete acknowledged legacy import marker.");
        }
        synchronized (LOCK) {
            dispatchedLegacyImports.remove(canonicalPath);
            restoredLegacyImportAcknowledged = true;
        }
    }

    static boolean consumeRestoredLegacyImportAcknowledgement() {
        synchronized (LOCK) {
            boolean acknowledged = restoredLegacyImportAcknowledged;
            restoredLegacyImportAcknowledged = false;
            return acknowledged;
        }
    }

    private static boolean drainReadyLegacyImportsLocked(Activity currentActivity) {
        File importDirectory = new File(currentActivity.getCacheDir(), "legacy-import");
        File[] candidates = importDirectory.listFiles();
        if (candidates == null) {
            return false;
        }
        boolean resumed = false;
        for (File marker : candidates) {
            String name = marker.getName();
            if (!marker.isFile() || !name.startsWith("legacy-v1-")
                    || !name.endsWith(".ready")) {
                continue;
            }
            File json = legacyJsonForReadyMarker(marker);
            if (!json.isFile()) {
                continue;
            }
            String canonicalPath = validateLegacyImportPath(currentActivity, json.getPath(), true);
            if (dispatchedLegacyImports.add(canonicalPath)) {
                sendUnityTo(unityGameObject, CALLBACK_DOCUMENT_PICKED, canonicalPath);
                resumed = true;
            }
        }
        return resumed;
    }

    private static String validateLegacyImportPath(
            Activity currentActivity, String absoluteCachePath, boolean requireReadyPair) {
        if (currentActivity == null || absoluteCachePath == null) {
            throw new IllegalArgumentException("Legacy import path is unavailable");
        }
        try {
            File importDirectory = new File(
                    currentActivity.getCacheDir(), "legacy-import").getCanonicalFile();
            File json = new File(absoluteCachePath).getCanonicalFile();
            String name = json.getName();
            if (!importDirectory.equals(json.getParentFile())
                    || !name.startsWith("legacy-v1-") || !name.endsWith(".json")) {
                throw new IllegalArgumentException("Legacy import path is outside app cache");
            }
            if (requireReadyPair
                    && (!json.isFile() || !legacyReadyMarkerForJson(json).isFile())) {
                throw new IllegalArgumentException("Legacy import is not a completed ready pair");
            }
            return json.getPath();
        } catch (IOException exception) {
            throw new IllegalArgumentException("Legacy import path could not be canonicalized", exception);
        }
    }

    private static File legacyReadyMarkerForJson(File json) {
        String name = json.getName();
        return new File(json.getParentFile(),
                name.substring(0, name.length() - ".json".length()) + ".ready");
    }

    private static File legacyJsonForReadyMarker(File marker) {
        String name = marker.getName();
        return new File(marker.getParentFile(),
                name.substring(0, name.length() - ".ready".length()) + ".json");
    }

    /** Opens the app-private transparent Activity that owns ACTION_OPEN_DOCUMENT. */
    public static void openLegacyDocumentPicker() {
        final Activity currentActivity = findUnityActivity();
        final String target = unityGameObject;
        if (currentActivity == null) {
            sendUnityTo(target, CALLBACK_DOCUMENT_ERROR,
                    "picker_unavailable:Unity activity is unavailable");
            return;
        }

        currentActivity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                try {
                    synchronized (LOCK) {
                        restoredLegacyImportAcknowledged = false;
                        pendingLegacyErrorTarget = null;
                        pendingLegacyErrorPayload = null;
                    }
                    Intent intent = new Intent(currentActivity, LegacyDocumentPickerActivity.class);
                    intent.putExtra(LegacyDocumentPickerActivity.EXTRA_UNITY_GAME_OBJECT, target);
                    currentActivity.startActivity(intent);
                } catch (RuntimeException exception) {
                    Log.e(TAG, "Unable to start legacy document picker.", exception);
                    sendUnityTo(target, CALLBACK_DOCUMENT_ERROR,
                            "picker_unavailable:" + safeMessage(exception));
                }
            }
        });
    }

    private static void startWindowTrackingLocked(final int requestGeneration) {
        final Activity trackedActivity = activity;
        try {
            windowExecutor = new Executor() {
                @Override
                public void execute(Runnable command) {
                    trackedActivity.runOnUiThread(command);
                }
            };
            windowConsumer = new Consumer<WindowLayoutInfo>() {
                @Override
                public void accept(WindowLayoutInfo windowLayoutInfo) {
                    if (!isLifecycleActive(requestGeneration, trackedActivity)) {
                        return;
                    }
                    dispatchRealWindowInfo(
                            trackedActivity, windowLayoutInfo, requestGeneration);
                }
            };
            windowTracker = new WindowInfoTrackerCallbackAdapter(
                    WindowInfoTracker.getOrCreate(trackedActivity));
            windowTracker.addWindowLayoutInfoListener(
                    trackedActivity, windowExecutor, windowConsumer);

            // Dispatch dimensions and insets immediately. Jetpack follows with fold data.
            dispatchRealWindowInfo(trackedActivity, null, requestGeneration);
        } catch (RuntimeException | LinkageError exception) {
            Log.e(TAG, "Unable to start Jetpack WindowManager tracking.", exception);
            latestRealWindowJson = emptyWindowJson("androidx-window-error");
            latestWindowJson = latestRealWindowJson;
            sendUnityIfLifecycleActive(
                    requestGeneration, trackedActivity, CALLBACK_WINDOW_LAYOUT, latestWindowJson);
        }
    }

    private static void dispatchRealWindowInfo(
            Activity sourceActivity, WindowLayoutInfo layoutInfo, int requestGeneration) {
        String json = buildWindowJson(sourceActivity, layoutInfo, "androidx-window");
        if (!isLifecycleActive(requestGeneration, sourceActivity)) {
            return;
        }
        latestRealWindowJson = json;
        if (debugWindowJson == null) {
            latestWindowJson = json;
            sendUnityIfLifecycleActive(
                    requestGeneration, sourceActivity, CALLBACK_WINDOW_LAYOUT, json);
        }
    }

    private static String buildWindowJson(
            Activity sourceActivity, WindowLayoutInfo layoutInfo, String source) {
        try {
            Rect windowBounds = currentWindowBounds(sourceActivity);
            int width = Math.max(0, windowBounds.width());
            int height = Math.max(0, windowBounds.height());
            InsetsValue safeInsets = readSafeInsets(sourceActivity);
            int safeWidth = Math.max(0, width - safeInsets.left - safeInsets.right);
            int safeHeight = Math.max(0, height - safeInsets.top - safeInsets.bottom);
            DisplayMetrics displayMetrics = sourceActivity.getResources().getDisplayMetrics();

            JSONObject root = new JSONObject();
            root.put("schemaVersion", 1);
            root.put("source", source);
            root.put("origin", "top-left");
            root.put("safeAreaOrigin", "top-left");
            root.put("widthPx", width);
            root.put("heightPx", height);
            root.put("densityDpi", displayMetrics.densityDpi);
            root.put("windowX", windowBounds.left);
            root.put("windowY", windowBounds.top);
            root.put("safeArea", rectJson(
                    safeInsets.left, safeInsets.top, safeWidth, safeHeight));
            root.put("timestampMs", System.currentTimeMillis());

            JSONArray foldingFeatures = new JSONArray();
            if (layoutInfo != null) {
                for (DisplayFeature displayFeature : layoutInfo.getDisplayFeatures()) {
                    if (!(displayFeature instanceof FoldingFeature)) {
                        continue;
                    }
                    FoldingFeature foldingFeature = (FoldingFeature) displayFeature;
                    Rect bounds = foldingFeature.getBounds();
                    JSONObject foldJson = new JSONObject();
                    foldJson.put("bounds", rectJson(
                            bounds.left, bounds.top, bounds.width(), bounds.height()));
                    foldJson.put("orientation", orientationName(foldingFeature));
                    foldJson.put("state", stateName(foldingFeature));
                    foldJson.put("occlusion", occlusionName(foldingFeature));
                    foldJson.put("separating", foldingFeature.isSeparating());
                    foldingFeatures.put(foldJson);
                }
            }
            root.put("foldingFeatures", foldingFeatures);
            return root.toString();
        } catch (RuntimeException | JSONException exception) {
            Log.e(TAG, "Unable to encode window layout information.", exception);
            return emptyWindowJson("window-json-error");
        }
    }

    private static Rect currentWindowBounds(Activity sourceActivity) {
        try {
            return WindowMetricsCalculator.getOrCreate()
                    .computeCurrentWindowMetrics(sourceActivity)
                    .getBounds();
        } catch (RuntimeException | LinkageError exception) {
            View decorView = sourceActivity.getWindow().getDecorView();
            int width = decorView.getWidth();
            int height = decorView.getHeight();
            if (width <= 0 || height <= 0) {
                DisplayMetrics metrics = sourceActivity.getResources().getDisplayMetrics();
                width = metrics.widthPixels;
                height = metrics.heightPixels;
            }
            return new Rect(0, 0, Math.max(0, width), Math.max(0, height));
        }
    }

    @SuppressWarnings("deprecation")
    private static InsetsValue readSafeInsets(Activity sourceActivity) {
        WindowInsets rootInsets = sourceActivity.getWindow()
                .getDecorView()
                .getRootWindowInsets();
        if (rootInsets == null) {
            return InsetsValue.NONE;
        }
        if (Build.VERSION.SDK_INT >= 30) {
            return Api30Insets.read(rootInsets);
        }

        int left = rootInsets.getStableInsetLeft();
        int top = rootInsets.getStableInsetTop();
        int right = rootInsets.getStableInsetRight();
        int bottom = rootInsets.getStableInsetBottom();
        if (Build.VERSION.SDK_INT >= 28) {
            InsetsValue cutoutInsets = Api28Insets.read(rootInsets);
            left = Math.max(left, cutoutInsets.left);
            top = Math.max(top, cutoutInsets.top);
            right = Math.max(right, cutoutInsets.right);
            bottom = Math.max(bottom, cutoutInsets.bottom);
        }
        return new InsetsValue(left, top, right, bottom);
    }

    private static JSONObject rectJson(int x, int y, int width, int height)
            throws JSONException {
        JSONObject rect = new JSONObject();
        rect.put("x", x);
        rect.put("y", y);
        rect.put("width", Math.max(0, width));
        rect.put("height", Math.max(0, height));
        return rect;
    }

    private static String orientationName(FoldingFeature feature) {
        return feature.getOrientation() == FoldingFeature.Orientation.HORIZONTAL
                ? "HORIZONTAL" : "VERTICAL";
    }

    private static String stateName(FoldingFeature feature) {
        return feature.getState() == FoldingFeature.State.HALF_OPENED
                ? "HALF_OPENED" : "FLAT";
    }

    private static String occlusionName(FoldingFeature feature) {
        return feature.getOcclusionType() == FoldingFeature.OcclusionType.FULL
                ? "FULL" : "NONE";
    }

    private static void registerBackCallbackLocked(final int requestGeneration) {
        if (Build.VERSION.SDK_INT < 33 || activity == null) {
            // API 26-32 is handled by the custom GameActivity KeyEvent path.
            return;
        }
        try {
            final Activity registeredActivity = activity;
            backCallback = Api33Back.register(registeredActivity, new Runnable() {
                @Override
                public void run() {
                    long sequence = dispatchBackIfLifecycleActive(
                            requestGeneration, registeredActivity);
                    if (sequence > 0L) {
                        Log.i(TAG, "STARFALL_ANDROID_BACK_PREDICTIVE_DISPATCH="
                                + Build.VERSION.SDK_INT + ":" + sequence);
                    }
                }
            });
        } catch (RuntimeException | LinkageError exception) {
            Log.e(TAG, "Unable to register Android 13 back callback.", exception);
            backCallback = null;
        }
    }

    private static void registerDebugReceiverLocked(final int requestGeneration) {
        if (!isApplicationDebuggable(activity)) {
            return;
        }
        final Activity registeredActivity = activity;
        debugReceiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context context, Intent intent) {
                if (!isLifecycleActive(requestGeneration, registeredActivity)) {
                    return;
                }
                if (DEBUG_COMMAND_ACTION.equals(intent.getAction())) {
                    dispatchDebugCommand(intent, requestGeneration, registeredActivity);
                    return;
                }
                if (!DEBUG_WINDOW_ACTION.equals(intent.getAction())) {
                    return;
                }
                try {
                    if (intent.getBooleanExtra("clear", false)) {
                        debugWindowJson = null;
                        latestWindowJson = latestRealWindowJson;
                    } else {
                        debugWindowJson = buildDebugWindowJson(intent);
                        latestWindowJson = debugWindowJson;
                    }
                    sendUnityIfLifecycleActive(
                            requestGeneration, registeredActivity,
                            CALLBACK_WINDOW_LAYOUT, latestWindowJson);
                } catch (RuntimeException | JSONException exception) {
                    Log.e(TAG, "Rejected invalid debug window broadcast.", exception);
                }
            }
        };

        IntentFilter filter = new IntentFilter(DEBUG_WINDOW_ACTION);
        filter.addAction(DEBUG_COMMAND_ACTION);
        if (Build.VERSION.SDK_INT >= 33) {
            // Exported only in DEBUG so adb can inject deterministic CI geometry.
            Api33Receiver.registerExported(registeredActivity, debugReceiver, filter);
        } else {
            registeredActivity.registerReceiver(debugReceiver, filter);
        }
    }

    private static boolean isApplicationDebuggable(Activity sourceActivity) {
        return sourceActivity != null
                && (sourceActivity.getApplicationInfo().flags & ApplicationInfo.FLAG_DEBUGGABLE) != 0;
    }

    private static boolean isLifecycleActive(int requestGeneration, Activity expectedActivity) {
        synchronized (LOCK) {
            return isLifecycleActiveLocked(requestGeneration, expectedActivity);
        }
    }

    private static boolean sendUnityIfLifecycleActive(
            int requestGeneration, Activity expectedActivity, String methodName, String payload) {
        synchronized (LOCK) {
            if (isLifecycleActiveLocked(requestGeneration, expectedActivity)) {
                sendUnity(methodName, payload);
                return true;
            }
            return false;
        }
    }

    /**
     * Dispatches API 26-32 Activity back presses through the same Unity callback as
     * Android 13 predictive back. Returns false while the managed bridge is not ready,
     * allowing the custom Activity to fall back to the platform implementation.
     */
    static boolean dispatchCompatBack(Activity sourceActivity) {
        synchronized (LOCK) {
            if (!initialized || activity != sourceActivity) {
                return false;
            }
            long sequence = dispatchBackLocked();
            Log.i(TAG, "STARFALL_ANDROID_BACK_COMPAT_DISPATCH="
                    + Build.VERSION.SDK_INT + ":" + sequence);
            return true;
        }
    }

    /** Caller must hold LOCK so lifecycle checks and the sequence stay atomic. */
    private static long dispatchBackLocked() {
        long sequence = ++backDispatchSequence;
        // UnitySendMessage is asynchronous. A unique payload makes every committed
        // Back independently observable across the native-to-managed player bridge.
        sendUnity(CALLBACK_BACK, Long.toString(sequence));
        return sequence;
    }

    private static long dispatchBackIfLifecycleActive(
            int requestGeneration, Activity expectedActivity) {
        synchronized (LOCK) {
            return isLifecycleActiveLocked(requestGeneration, expectedActivity)
                    ? dispatchBackLocked()
                    : 0L;
        }
    }

    private static boolean isLifecycleActiveLocked(
            int requestGeneration, Activity expectedActivity) {
        return initialized
                && LIFECYCLE.isCurrent(requestGeneration)
                && activity == expectedActivity;
    }

    private static void dispatchDebugCommand(
            Intent intent, int requestGeneration, Activity registeredActivity) {
        try {
            int requestId = intent.getIntExtra("requestId", 0);
            String command = intent.getStringExtra("command");
            if (requestId <= 0) {
                throw new IllegalArgumentException("requestId must be positive");
            }
            if (!isAllowedDebugCommand(command)) {
                throw new IllegalArgumentException("command is not in the fixed CI whitelist");
            }
            JSONObject request = new JSONObject();
            request.put("requestId", requestId);
            request.put("command", command);
            String payload = request.toString();
            if (sendUnityIfLifecycleActive(
                    requestGeneration, registeredActivity, CALLBACK_CI_COMMAND, payload)) {
                Log.i(TAG, "STARFALL_ANDROID_CI_COMMAND_DISPATCH=" + payload);
            }
        } catch (RuntimeException | JSONException exception) {
            Log.e(TAG, "STARFALL_ANDROID_CI_COMMAND_ERR=" + safeMessage(exception));
        }
    }

    private static boolean isAllowedDebugCommand(String command) {
        if (command == null) {
            return false;
        }
        switch (command) {
            case "status":
            case "start-new-game":
            case "undock":
            case "prepare-touch-target":
            case "select-first-station":
            case "select-first-gate":
            case "select-first-hostile":
            case "lock-selected":
            case "show-death-overlay":
            case "warp-selected":
            case "dock-or-jump":
            case "activate-modules":
            case "save":
            case "seed-low-memory-fixture":
                return true;
            default:
                return false;
        }
    }

    private static String buildDebugWindowJson(Intent intent) throws JSONException {
        String rawJson = intent.getStringExtra("json");
        JSONObject root;
        if (rawJson != null) {
            if (rawJson.length() > 65536) {
                throw new IllegalArgumentException("Debug window JSON exceeds 64 KiB");
            }
            root = new JSONObject(rawJson);
        } else {
            root = new JSONObject(latestRealWindowJson);
        }

        int width = integerExtra(intent, "widthPx", root.optInt("widthPx", 0));
        int height = integerExtra(intent, "heightPx", root.optInt("heightPx", 0));
        int density = integerExtra(intent, "densityDpi", root.optInt("densityDpi", 160));
        if (width <= 0 || height <= 0 || density <= 0) {
            throw new IllegalArgumentException("widthPx, heightPx, and densityDpi must be positive");
        }

        JSONObject existingSafe = root.optJSONObject("safeArea");
        int safeX = integerExtra(intent, "safeX",
                existingSafe == null ? 0 : existingSafe.optInt("x", 0));
        int safeY = integerExtra(intent, "safeY",
                existingSafe == null ? 0 : existingSafe.optInt("y", 0));
        int safeWidth = integerExtra(intent, "safeWidth",
                existingSafe == null ? width : existingSafe.optInt("width", width));
        int safeHeight = integerExtra(intent, "safeHeight",
                existingSafe == null ? height : existingSafe.optInt("height", height));
        if (safeX < 0 || safeY < 0 || safeWidth < 0 || safeHeight < 0
                || safeX + safeWidth > width || safeY + safeHeight > height) {
            throw new IllegalArgumentException("safe area must remain inside the window");
        }

        root.put("schemaVersion", 1);
        root.put("source", "debug-broadcast");
        root.put("origin", "top-left");
        root.put("safeAreaOrigin", "top-left");
        root.put("widthPx", width);
        root.put("heightPx", height);
        root.put("densityDpi", density);
        root.put("safeArea", rectJson(safeX, safeY, safeWidth, safeHeight));
        root.put("timestampMs", System.currentTimeMillis());

        boolean hasFoldFields = intent.hasExtra("foldX")
                || intent.hasExtra("foldY")
                || intent.hasExtra("foldWidth")
                || intent.hasExtra("foldHeight");
        if (intent.getBooleanExtra("noFold", false)) {
            root.put("foldingFeatures", new JSONArray());
        } else if (hasFoldFields) {
            int foldX = intent.getIntExtra("foldX", 0);
            int foldY = intent.getIntExtra("foldY", 0);
            int foldWidth = intent.getIntExtra("foldWidth", 0);
            int foldHeight = intent.getIntExtra("foldHeight", 0);
            if (foldX < 0 || foldY < 0 || foldWidth < 0 || foldHeight < 0
                    || foldX + foldWidth > width || foldY + foldHeight > height) {
                throw new IllegalArgumentException("fold bounds must remain inside the window");
            }
            JSONObject fold = new JSONObject();
            fold.put("bounds", rectJson(foldX, foldY, foldWidth, foldHeight));
            fold.put("orientation", enumExtra(intent, "orientation", "VERTICAL",
                    "HORIZONTAL", "VERTICAL"));
            fold.put("state", enumExtra(intent, "state", "FLAT",
                    "FLAT", "HALF_OPENED"));
            fold.put("occlusion", enumExtra(intent, "occlusion", "NONE",
                    "NONE", "FULL"));
            fold.put("separating", intent.getBooleanExtra("separating", false));
            root.put("foldingFeatures", new JSONArray().put(fold));
        } else if (root.optJSONArray("foldingFeatures") == null) {
            root.put("foldingFeatures", new JSONArray());
        }
        return root.toString();
    }

    private static int integerExtra(Intent intent, String name, int fallback) {
        return intent.hasExtra(name) ? intent.getIntExtra(name, fallback) : fallback;
    }

    private static String enumExtra(
            Intent intent, String name, String fallback, String first, String second) {
        String value = intent.getStringExtra(name);
        if (value == null) {
            return fallback;
        }
        value = value.trim().toUpperCase(Locale.ROOT);
        if (!first.equals(value) && !second.equals(value)) {
            throw new IllegalArgumentException(name + " must be " + first + " or " + second);
        }
        return value;
    }

    private static void stopLocked() {
        initialized = false;
        if (windowTracker != null && windowConsumer != null) {
            try {
                windowTracker.removeWindowLayoutInfoListener(windowConsumer);
            } catch (RuntimeException exception) {
                Log.w(TAG, "Unable to remove window listener cleanly.", exception);
            }
        }
        windowTracker = null;
        windowConsumer = null;
        windowExecutor = null;

        if (Build.VERSION.SDK_INT >= 33 && activity != null && backCallback != null) {
            try {
                Api33Back.unregister(activity, backCallback);
            } catch (RuntimeException | LinkageError exception) {
                Log.w(TAG, "Unable to remove Android 13 back callback cleanly.", exception);
            }
        }
        backCallback = null;

        if (activity != null && debugReceiver != null) {
            try {
                activity.unregisterReceiver(debugReceiver);
            } catch (IllegalArgumentException exception) {
                Log.w(TAG, "Debug receiver was already unregistered.", exception);
            }
        }
        debugReceiver = null;
        debugWindowJson = null;
        latestWindowJson = latestRealWindowJson;
        dispatchedLegacyImports.clear();
        activity = null;
    }

    private static String normalizeUnityGameObject(String value) {
        if (value == null || value.trim().isEmpty()) {
            return DEFAULT_UNITY_GAME_OBJECT;
        }
        return value.trim();
    }

    private static Activity findUnityActivity() {
        try {
            Class<?> unityPlayer = Class.forName("com.unity3d.player.UnityPlayer");
            Field currentActivity = unityPlayer.getField("currentActivity");
            Object value = currentActivity.get(null);
            return value instanceof Activity ? (Activity) value : null;
        } catch (ReflectiveOperationException | LinkageError exception) {
            Log.e(TAG, "Unable to resolve UnityPlayer.currentActivity.", exception);
            return null;
        }
    }

    private static void sendUnity(String methodName, String payload) {
        sendUnityTo(unityGameObject, methodName, payload);
    }

    static void sendUnityTo(String targetGameObject, String methodName, String payload) {
        final String target = normalizeUnityGameObject(targetGameObject);
        final String safePayload = payload == null ? "" : payload;
        try {
            Class<?> unityPlayer = Class.forName("com.unity3d.player.UnityPlayer");
            Method unitySendMessage = unityPlayer.getMethod(
                    "UnitySendMessage", String.class, String.class, String.class);
            unitySendMessage.invoke(null, target, methodName, safePayload);
        } catch (ReflectiveOperationException | LinkageError exception) {
            Log.e(TAG, "Unable to dispatch Unity callback " + methodName + ".", exception);
        }
    }

    static String safeMessage(Throwable throwable) {
        String value = throwable == null ? null : throwable.getMessage();
        if (value == null || value.trim().isEmpty()) {
            value = throwable == null ? "unknown error" : throwable.getClass().getSimpleName();
        }
        value = value.replace('\n', ' ').replace('\r', ' ').trim();
        return value.length() <= 240 ? value : value.substring(0, 240);
    }

    private static String emptyWindowJson(String source) {
        return "{\"schemaVersion\":1,\"source\":\"" + source
                + "\",\"origin\":\"top-left\",\"safeAreaOrigin\":\"top-left\""
                + ",\"widthPx\":0,\"heightPx\":0,\"densityDpi\":0"
                + ",\"safeArea\":{\"x\":0,\"y\":0,\"width\":0,\"height\":0}"
                + ",\"foldingFeatures\":[]}";
    }

    private static final class InsetsValue {
        static final InsetsValue NONE = new InsetsValue(0, 0, 0, 0);

        final int left;
        final int top;
        final int right;
        final int bottom;

        InsetsValue(int left, int top, int right, int bottom) {
            this.left = Math.max(0, left);
            this.top = Math.max(0, top);
            this.right = Math.max(0, right);
            this.bottom = Math.max(0, bottom);
        }
    }

    private static final class Api28Insets {
        private Api28Insets() {
        }

        static InsetsValue read(WindowInsets windowInsets) {
            DisplayCutout cutout = windowInsets.getDisplayCutout();
            if (cutout == null) {
                return InsetsValue.NONE;
            }
            return new InsetsValue(
                    cutout.getSafeInsetLeft(),
                    cutout.getSafeInsetTop(),
                    cutout.getSafeInsetRight(),
                    cutout.getSafeInsetBottom());
        }
    }

    private static final class Api30Insets {
        private Api30Insets() {
        }

        static InsetsValue read(WindowInsets windowInsets) {
            android.graphics.Insets insets = windowInsets.getInsetsIgnoringVisibility(
                    WindowInsets.Type.systemBars() | WindowInsets.Type.displayCutout());
            return new InsetsValue(insets.left, insets.top, insets.right, insets.bottom);
        }
    }

    private static final class Api33Back {
        private Api33Back() {
        }

        static Object register(Activity sourceActivity, final Runnable callback) {
            android.window.OnBackInvokedCallback nativeCallback =
                    new android.window.OnBackInvokedCallback() {
                        @Override
                        public void onBackInvoked() {
                            callback.run();
                        }
                    };
            sourceActivity.getOnBackInvokedDispatcher().registerOnBackInvokedCallback(
                    // Unity GameActivity registers its own higher-than-default callback.
                    // Keep Starfall's overlay/navigation router ahead of it so every
                    // committed Back reaches the currently visible game overlay.
                    android.window.OnBackInvokedDispatcher.PRIORITY_OVERLAY,
                    nativeCallback);
            return nativeCallback;
        }

        static void unregister(Activity sourceActivity, Object callback) {
            sourceActivity.getOnBackInvokedDispatcher().unregisterOnBackInvokedCallback(
                    (android.window.OnBackInvokedCallback) callback);
        }
    }

    private static final class Api33Receiver {
        private Api33Receiver() {
        }

        static void registerExported(
                Context context, BroadcastReceiver receiver, IntentFilter filter) {
            context.registerReceiver(receiver, filter, Context.RECEIVER_EXPORTED);
        }
    }
}
