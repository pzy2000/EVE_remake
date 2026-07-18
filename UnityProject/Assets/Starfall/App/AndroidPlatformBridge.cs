using System;
using UnityEngine;

namespace Starfall.App
{
    internal static class AndroidPlatformBridge
    {
        private const string JavaClassName = "com.pzy.starfall.mobile.StarfallMobileBridge";

        public static event Action<string> WindowLayoutInfoReceived;

        public static void Initialize(string unityGameObject)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var bridge = new AndroidJavaClass(JavaClassName);
                bridge.CallStatic("initialize", unityGameObject);
                var initialMetrics = bridge.CallStatic<string>("getLatestWindowLayoutInfoJson");
                if (!string.IsNullOrEmpty(initialMetrics)) PublishWindowLayoutInfo(initialMetrics);
            }
            catch (Exception exception)
            {
                Debug.LogError("Android native bridge initialization failed; using Unity window metrics: " +
                               exception.Message);
            }
#endif
        }

        public static void Shutdown()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using var bridge = new AndroidJavaClass(JavaClassName);
                bridge.CallStatic("shutdown");
#endif
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Android native bridge shutdown failed: " + exception.Message);
            }
            finally
            {
                WindowLayoutInfoReceived = null;
            }
        }

        public static bool OpenLegacyDocumentPicker(out string error)
        {
            error = string.Empty;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var bridge = new AndroidJavaClass(JavaClassName);
                bridge.CallStatic("openLegacyDocumentPicker");
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Debug.LogError("Android document picker failed to start: " + exception.Message);
                return false;
            }
#else
            error = "Android document picker is unavailable on this platform.";
            return false;
#endif
        }

        public static bool TryGetLegacyImportCacheRoot(out string cacheRoot, out string error)
        {
            cacheRoot = string.Empty;
            error = string.Empty;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var bridge = new AndroidJavaClass(JavaClassName);
                cacheRoot = bridge.CallStatic<string>("getLegacyImportCacheRoot") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(cacheRoot)) return true;
                error = "Android returned an empty app cache directory.";
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Debug.LogError("Android legacy import cache lookup failed: " + exception.Message);
                return false;
            }
#else
            error = "Android app cache is unavailable on this platform.";
            return false;
#endif
        }

        public static void AcknowledgeLegacyDocument(string absoluteCachePath)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrWhiteSpace(absoluteCachePath)) return;
            try
            {
                using var bridge = new AndroidJavaClass(JavaClassName);
                bridge.CallStatic("acknowledgeLegacyDocument", absoluteCachePath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Android legacy import acknowledgement failed: " + exception.Message);
            }
#endif
        }

        public static void PublishWindowLayoutInfo(string json)
        {
            WindowLayoutInfoReceived?.Invoke(json ?? string.Empty);
        }
    }
}
