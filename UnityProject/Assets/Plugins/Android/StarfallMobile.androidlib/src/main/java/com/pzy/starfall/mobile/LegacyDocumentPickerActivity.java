package com.pzy.starfall.mobile;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.JsonReader;
import android.util.JsonToken;
import android.util.Log;

import java.io.BufferedInputStream;
import java.io.BufferedOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicBoolean;

/** Transparent, non-exported owner of the one-shot Storage Access Framework flow. */
public final class LegacyDocumentPickerActivity extends Activity {
    public static final String EXTRA_UNITY_GAME_OBJECT =
            "com.pzy.starfall.mobile.extra.UNITY_GAME_OBJECT";

    private static final String TAG = "StarfallLegacyPicker";
    private static final String STATE_OWNS_ACTIVE = "starfall.ownsActivePicker";
    private static final String STATE_UNITY_TARGET = "starfall.unityTarget";
    private static final String STATE_RESULT_CONSUMED = "starfall.resultConsumed";
    private static final String STATE_COPY_STARTED = "starfall.copyStarted";
    private static final String STATE_SOURCE_URI = "starfall.sourceUri";
    private static final int REQUEST_OPEN_DOCUMENT = 4107;
    private static final long MAX_DOCUMENT_BYTES = 5L * 1024L * 1024L;
    private static final AtomicBoolean ACTIVE = new AtomicBoolean(false);
    private static final Object IMPORT_LOCK = new Object();

    private static ImportWork activeWork;
    private static LegacyDocumentPickerActivity workOwner;

    private String unityGameObject;
    private boolean ownsActive;
    private boolean resultConsumed;
    private boolean copyStarted;
    private String sourceUriString;
    private final AtomicBoolean callbackSent = new AtomicBoolean(false);

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        unityGameObject = getIntent().getStringExtra(EXTRA_UNITY_GAME_OBJECT);
        if (savedInstanceState != null) {
            unityGameObject = savedInstanceState.getString(STATE_UNITY_TARGET, unityGameObject);
            ownsActive = savedInstanceState.getBoolean(STATE_OWNS_ACTIVE, false);
            resultConsumed = savedInstanceState.getBoolean(STATE_RESULT_CONSUMED, false);
            copyStarted = savedInstanceState.getBoolean(STATE_COPY_STARTED, false);
            sourceUriString = savedInstanceState.getString(STATE_SOURCE_URI);
            if (ownsActive) {
                ACTIVE.set(true);
            }
        } else {
            ownsActive = ACTIVE.compareAndSet(false, true);
        }

        if (!ownsActive) {
            deliverError("picker_busy", "A legacy document picker is already active");
            return;
        }

        synchronized (IMPORT_LOCK) {
            workOwner = this;
        }

        if (savedInstanceState != null
                && StarfallMobileBridge.consumeRestoredLegacyImportAcknowledgement()) {
            releaseActive();
            finish();
            return;
        }

        if (savedInstanceState == null) {
            launchOpenDocument();
        } else if (resultConsumed && copyStarted) {
            if (sourceUriString == null || sourceUriString.trim().isEmpty()) {
                deliverError(
                        "import_interrupted",
                        "Legacy import was interrupted before its document URI was saved");
            } else if (!deliverExistingReadyImport()) {
                startCopy(Uri.parse(sourceUriString));
            }
        } else if (resultConsumed) {
            deliverError("import_interrupted", "Legacy import copy state could not be restored");
        }
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        super.onSaveInstanceState(outState);
        outState.putBoolean(STATE_OWNS_ACTIVE, ownsActive);
        outState.putString(STATE_UNITY_TARGET, unityGameObject);
        outState.putBoolean(STATE_RESULT_CONSUMED, resultConsumed);
        outState.putBoolean(STATE_COPY_STARTED, copyStarted);
        outState.putString(STATE_SOURCE_URI, sourceUriString);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_OPEN_DOCUMENT || resultConsumed) {
            return;
        }
        resultConsumed = true;

        if (resultCode != RESULT_OK) {
            deliverError("cancelled", "Document selection was cancelled");
            return;
        }
        if (data == null || data.getData() == null) {
            deliverError("missing_uri", "Document provider returned no URI");
            return;
        }

        final Uri sourceUri = data.getData();
        sourceUriString = sourceUri.toString();
        startCopy(sourceUri);
    }

    private void startCopy(final Uri sourceUri) {
        copyStarted = true;
        final ImportWork work;
        final boolean shouldStart;
        synchronized (IMPORT_LOCK) {
            if (activeWork == null) {
                cleanupIncompleteImportsBeforeWork();
                activeWork = new ImportWork(sourceUri.toString(), unityGameObject);
                workOwner = this;
                shouldStart = true;
            } else if (activeWork.matches(sourceUri.toString(), unityGameObject)) {
                workOwner = this;
                shouldStart = false;
            } else {
                deliverError(
                        "picker_busy", "A different legacy import copy is already active");
                return;
            }
            work = activeWork;
        }

        if (!shouldStart) {
            dispatchCompletedWork(work);
            return;
        }
        new Thread(new Runnable() {
            @Override
            public void run() {
                copyAndValidate(sourceUri, work);
            }
        }, "StarfallLegacyImport").start();
    }

    private void cleanupIncompleteImportsBeforeWork() {
        File importDirectory = new File(getCacheDir(), "legacy-import");
        File[] candidates = importDirectory.listFiles();
        if (candidates == null) {
            return;
        }
        for (File candidate : candidates) {
            String name = candidate.getName();
            if (!candidate.isFile() || !name.startsWith("legacy-v1-")) {
                continue;
            }
            boolean incomplete = name.endsWith(".part");
            if (name.endsWith(".ready")) {
                String jsonName = name.substring(
                        0, name.length() - ".ready".length()) + ".json";
                incomplete = !new File(importDirectory, jsonName).isFile();
            } else if (name.endsWith(".json")) {
                String readyName = name.substring(
                        0, name.length() - ".json".length()) + ".ready";
                incomplete = !new File(importDirectory, readyName).isFile();
            }
            if (incomplete && !candidate.delete()) {
                Log.w(TAG, "Unable to remove an interrupted legacy import file: " + name);
            }
        }
    }

    @Override
    protected void onDestroy() {
        synchronized (IMPORT_LOCK) {
            if (workOwner == this) {
                workOwner = null;
            }
        }
        if (ownsActive && !isChangingConfigurations() && (isFinishing() || callbackSent.get())) {
            releaseActive();
        }
        super.onDestroy();
    }

    private void launchOpenDocument() {
        try {
            Intent intent = new Intent(Intent.ACTION_OPEN_DOCUMENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            // Android requires a wildcard primary type when EXTRA_MIME_TYPES
            // contains more than one alternative. The strict metadata and
            // content checks below remain the security boundary even if a
            // provider ignores the requested MIME filters.
            intent.setType("*/*");
            intent.putExtra(Intent.EXTRA_MIME_TYPES,
                    new String[]{"application/json", "text/json"});
            intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            startActivityForResult(intent, REQUEST_OPEN_DOCUMENT);
        } catch (RuntimeException exception) {
            Log.e(TAG, "Unable to launch ACTION_OPEN_DOCUMENT.", exception);
            deliverError("picker_unavailable", StarfallMobileBridge.safeMessage(exception));
        }
    }

    private void copyAndValidate(Uri sourceUri, ImportWork work) {
        File partial = null;
        File destination = null;
        File readyMarker = null;
        try {
            ContentResolver resolver = getContentResolver();
            DocumentMetadata metadata = readMetadata(resolver, sourceUri);
            if (!metadata.isJson()) {
                throw new ImportException(
                        "invalid_type", "Selected document is not a JSON file");
            }
            if (metadata.size >= 0 && metadata.size > MAX_DOCUMENT_BYTES) {
                throw new ImportException(
                        "too_large", "JSON exceeds the 5242880 byte limit");
            }

            File importDirectory = new File(getCacheDir(), "legacy-import");
            if (!importDirectory.exists() && !importDirectory.mkdirs()) {
                throw new ImportException(
                        "copy_failed", "Unable to create the legacy import cache directory");
            }
            partial = File.createTempFile("legacy-v1-", ".part", importDirectory);
            streamCopyWithLimit(resolver, sourceUri, partial);
            validateJsonObject(partial);

            String partialName = partial.getName();
            String publishedName = partialName.substring(
                    0, partialName.length() - ".part".length()) + ".json";
            destination = new File(importDirectory, publishedName);
            readyMarker = new File(
                    importDirectory,
                    publishedName.substring(0, publishedName.length() - ".json".length())
                            + ".ready");
            writeReadyMarker(readyMarker);
            if (!partial.renameTo(destination)) {
                throw new ImportException(
                        "copy_failed", "Unable to publish the validated legacy save atomically");
            }
            partial = null;

            completeWorkSuccess(work, destination.getAbsolutePath());
            destination = null; // The C# importer owns successful cache-file cleanup.
            readyMarker = null; // The C# acknowledgement owns successful marker cleanup.
        } catch (ImportException exception) {
            completeWorkError(work, exception.code, exception.getMessage());
        } catch (SecurityException exception) {
            Log.e(TAG, "Document provider denied read access.", exception);
            completeWorkError(work, "open_failed", StarfallMobileBridge.safeMessage(exception));
        } catch (IOException | RuntimeException exception) {
            Log.e(TAG, "Legacy document import failed.", exception);
            completeWorkError(work, "copy_failed", StarfallMobileBridge.safeMessage(exception));
        } finally {
            if (partial != null && partial.exists() && !partial.delete()) {
                Log.w(TAG, "Unable to delete partial legacy import cache file.");
            }
            if (destination != null && destination.exists() && !destination.delete()) {
                Log.w(TAG, "Unable to delete unpublished legacy import cache file.");
            }
            if (readyMarker != null && readyMarker.exists() && !readyMarker.delete()) {
                Log.w(TAG, "Unable to delete unpublished legacy import marker.");
            }
        }
    }

    private boolean deliverExistingReadyImport() {
        synchronized (IMPORT_LOCK) {
            if (activeWork != null) {
                return false;
            }
        }
        File importDirectory = new File(getCacheDir(), "legacy-import");
        File[] markers = importDirectory.listFiles();
        if (markers == null) {
            return false;
        }

        File completed = null;
        for (File marker : markers) {
            String markerName = marker.getName();
            if (!marker.isFile() || !markerName.startsWith("legacy-v1-")
                    || !markerName.endsWith(".ready")) {
                continue;
            }
            String jsonName = markerName.substring(
                    0, markerName.length() - ".ready".length()) + ".json";
            File candidate = new File(importDirectory, jsonName);
            if (!candidate.isFile()) {
                if (!marker.delete()) {
                    Log.w(TAG, "Unable to delete an incomplete legacy import marker.");
                }
                continue;
            }
            if (completed != null) {
                deliverError(
                        "import_interrupted", "Multiple completed legacy imports require cleanup");
                return true;
            }
            completed = candidate;
        }
        if (completed == null) {
            return false;
        }

        deliverSuccess(completed.getAbsolutePath());
        return true;
    }

    private static void writeReadyMarker(File marker) throws IOException {
        try (FileOutputStream output = new FileOutputStream(marker)) {
            output.write(1);
            output.flush();
            output.getFD().sync();
        }
    }

    private static void completeWorkSuccess(ImportWork work, String path) {
        completeWork(work, path, null, null);
    }

    private static void completeWorkError(ImportWork work, String code, String message) {
        completeWork(work, null, code, message);
    }

    private static void completeWork(
            ImportWork work, String path, String errorCode, String errorMessage) {
        LegacyDocumentPickerActivity owner;
        synchronized (IMPORT_LOCK) {
            if (activeWork != work || work.completed) {
                return;
            }
            work.completed = true;
            work.completedPath = path;
            work.errorCode = errorCode;
            work.errorMessage = errorMessage;
            owner = workOwner;
        }
        if (owner != null) {
            owner.dispatchCompletedWork(work);
        }
    }

    private void dispatchCompletedWork(final ImportWork work) {
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                String path;
                String errorCode;
                String errorMessage;
                synchronized (IMPORT_LOCK) {
                    if (activeWork != work || workOwner != LegacyDocumentPickerActivity.this
                            || !work.completed || work.delivered) {
                        return;
                    }
                    work.delivered = true;
                    path = work.completedPath;
                    errorCode = work.errorCode;
                    errorMessage = work.errorMessage;
                    activeWork = null;
                    workOwner = null;
                }
                if (path != null) {
                    deliverSuccess(path);
                } else {
                    deliverError(errorCode, errorMessage);
                }
            }
        });
    }

    private static DocumentMetadata readMetadata(ContentResolver resolver, Uri uri) {
        String mimeType = resolver.getType(uri);
        String displayName = null;
        long size = -1L;
        try (Cursor cursor = resolver.query(
                uri,
                new String[]{OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE},
                null,
                null,
                null)) {
            if (cursor != null && cursor.moveToFirst()) {
                int nameColumn = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                int sizeColumn = cursor.getColumnIndex(OpenableColumns.SIZE);
                if (nameColumn >= 0 && !cursor.isNull(nameColumn)) {
                    displayName = cursor.getString(nameColumn);
                }
                if (sizeColumn >= 0 && !cursor.isNull(sizeColumn)) {
                    size = cursor.getLong(sizeColumn);
                }
            }
        } catch (RuntimeException exception) {
            // Metadata is advisory. The bounded stream copy remains authoritative.
            Log.w(TAG, "Document provider did not expose metadata.", exception);
        }
        return new DocumentMetadata(mimeType, displayName, size);
    }

    private static void streamCopyWithLimit(
            ContentResolver resolver, Uri sourceUri, File destination)
            throws IOException, ImportException {
        InputStream rawInput = resolver.openInputStream(sourceUri);
        if (rawInput == null) {
            throw new ImportException("open_failed", "Document provider returned no stream");
        }

        long copied = 0L;
        try (InputStream input = new BufferedInputStream(rawInput);
             FileOutputStream fileOutput = new FileOutputStream(destination);
             OutputStream output = new BufferedOutputStream(fileOutput)) {
            byte[] buffer = new byte[32 * 1024];
            int read;
            while ((read = input.read(buffer)) != -1) {
                if (copied + read > MAX_DOCUMENT_BYTES) {
                    throw new ImportException(
                            "too_large", "JSON exceeds the 5242880 byte limit");
                }
                output.write(buffer, 0, read);
                copied += read;
            }
            output.flush();
            fileOutput.getFD().sync();
        }
    }

    private static void validateJsonObject(File file) throws ImportException {
        try (JsonReader reader = new JsonReader(new InputStreamReader(
                new FileInputStream(file), StandardCharsets.UTF_8))) {
            reader.setLenient(false);
            if (reader.peek() != JsonToken.BEGIN_OBJECT) {
                throw new ImportException(
                        "invalid_json", "Legacy save JSON root must be an object");
            }
            reader.beginObject();
            while (reader.hasNext()) {
                reader.nextName();
                reader.skipValue();
            }
            reader.endObject();
            if (reader.peek() != JsonToken.END_DOCUMENT) {
                throw new ImportException(
                        "invalid_json", "JSON contains trailing data");
            }
        } catch (ImportException exception) {
            throw exception;
        } catch (IOException | RuntimeException exception) {
            throw new ImportException(
                    "invalid_json", "Selected document is not valid JSON");
        }
    }

    private void deliverSuccess(final String absoluteCachePath) {
        if (!callbackSent.compareAndSet(false, true)) {
            return;
        }
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                releaseActive();
                StarfallMobileBridge.notifyLegacyDocumentReady(
                        unityGameObject, absoluteCachePath);
                finish();
            }
        });
    }

    private void deliverError(final String code, final String message) {
        if (!callbackSent.compareAndSet(false, true)) {
            return;
        }
        final String payload = code + ":" + sanitizeMessage(message);
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                releaseActive();
                StarfallMobileBridge.notifyLegacyDocumentError(unityGameObject, payload);
                finish();
            }
        });
    }

    private void releaseActive() {
        if (ownsActive) {
            ACTIVE.set(false);
            ownsActive = false;
        }
    }

    private static String sanitizeMessage(String message) {
        String safe = message == null ? "unknown error" : message;
        safe = safe.replace('\n', ' ').replace('\r', ' ').trim();
        return safe.length() <= 240 ? safe : safe.substring(0, 240);
    }

    private static final class DocumentMetadata {
        final String mimeType;
        final String displayName;
        final long size;

        DocumentMetadata(String mimeType, String displayName, long size) {
            this.mimeType = mimeType;
            this.displayName = displayName;
            this.size = size;
        }

        boolean isJson() {
            String mime = mimeType == null ? "" : mimeType.toLowerCase(Locale.ROOT);
            boolean jsonMime = "application/json".equals(mime)
                    || "text/json".equals(mime)
                    || mime.endsWith("+json");
            boolean jsonName = displayName != null
                    && displayName.toLowerCase(Locale.ROOT).endsWith(".json");
            if (jsonMime || jsonName) {
                return true;
            }

            // A few SAF providers expose no metadata (or only a generic binary MIME).
            // In that case strict content validation below is authoritative.
            boolean genericMime = mime.isEmpty() || "application/octet-stream".equals(mime);
            boolean missingName = displayName == null || displayName.trim().isEmpty();
            return genericMime && missingName;
        }
    }

    private static final class ImportWork {
        final String sourceUri;
        final String unityTarget;
        boolean completed;
        boolean delivered;
        String completedPath;
        String errorCode;
        String errorMessage;

        ImportWork(String sourceUri, String unityTarget) {
            this.sourceUri = sourceUri;
            this.unityTarget = normalizeTarget(unityTarget);
        }

        boolean matches(String otherSourceUri, String otherUnityTarget) {
            return sourceUri.equals(otherSourceUri)
                    && unityTarget.equals(normalizeTarget(otherUnityTarget));
        }

        private static String normalizeTarget(String value) {
            return value == null || value.trim().isEmpty() ? "StarfallApp" : value.trim();
        }
    }

    private static final class ImportException extends Exception {
        private static final long serialVersionUID = 1L;
        final String code;

        ImportException(String code, String message) {
            super(message);
            this.code = code;
        }
    }
}
