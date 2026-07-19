using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Keeps Android UI Toolkit content on its retained-mode panel, but routes
    /// the panel through an explicit transparent render texture before the
    /// final IMGUI composition. This avoids device-specific overlay submission
    /// failures without changing the UXML tree or its input routing.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class AndroidUiRenderTextureCompositor : MonoBehaviour, IDisposable
    {
        private const int CompositionGuiDepth = 1000;

        private PanelSettings panelSettings;
        private RenderTexture originalTargetTexture;
        private RenderTexture uiTexture;
        private Color originalClearColorValue;
        private bool originalClearColor;
        private bool configured;
        private bool disposed;

        public static AndroidUiRenderTextureCompositor Attach(UIDocument document)
        {
            if (document == null || document.panelSettings == null) return null;
            var compositor = document.GetComponent<AndroidUiRenderTextureCompositor>();
            if (compositor == null)
                compositor = document.gameObject.AddComponent<AndroidUiRenderTextureCompositor>();
            compositor.Configure(document.panelSettings);
            return compositor;
        }

        private void Configure(PanelSettings settings)
        {
            if (configured && ReferenceEquals(panelSettings, settings)) return;
            ReleaseResources();

            panelSettings = settings;
            originalTargetTexture = settings.targetTexture;
            originalClearColor = settings.clearColor;
            originalClearColorValue = settings.colorClearValue;
            settings.clearColor = true;
            settings.colorClearValue = Color.clear;
            // UI Toolkit receives top-left screen coordinates. Because this
            // texture is composited one-to-one over the full screen, the mapping
            // remains the overlay identity transform.
            settings.SetScreenToPanelSpaceFunction(ScreenToPanelSpace);
            configured = true;
            EnsureTexture();
        }

        private static Vector2 ScreenToPanelSpace(Vector2 screenPosition) => screenPosition;

        private void Update()
        {
            if (!disposed) EnsureTexture();
        }

        private void EnsureTexture()
        {
            if (!configured || panelSettings == null) return;
            var width = Mathf.Max(1, Screen.width);
            var height = Mathf.Max(1, Screen.height);
            if (uiTexture != null && uiTexture.width == width && uiTexture.height == height)
                return;

            ReleaseTexture();
            var readWrite = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? RenderTextureReadWrite.sRGB
                : RenderTextureReadWrite.Linear;
            uiTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, readWrite)
            {
                name = $"Starfall Android UI {width}x{height}",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.DontSave,
            };
            uiTexture.Create();
            panelSettings.targetTexture = uiTexture;
            Debug.Log($"[Starfall Android] UI panel compositor ready: {width}x{height}.");
        }

        private void OnGUI()
        {
            if (disposed || uiTexture == null || Event.current.type != EventType.Repaint) return;

            var previousDepth = GUI.depth;
            var previousColor = GUI.color;
            GUI.depth = CompositionGuiDepth;
            GUI.color = Color.white;
            GUI.DrawTexture(
                new Rect(0f, 0f, Screen.width, Screen.height),
                uiTexture,
                ScaleMode.StretchToFill,
                true);
            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleaseResources();
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (!disposed) ReleaseResources();
            disposed = true;
        }

        private void ReleaseResources()
        {
            if (configured && panelSettings != null)
            {
                var ownsPanelTarget = ReferenceEquals(panelSettings.targetTexture, uiTexture);
                if (ownsPanelTarget)
                {
                    panelSettings.targetTexture = originalTargetTexture;
                    panelSettings.clearColor = originalClearColor;
                    panelSettings.colorClearValue = originalClearColorValue;
                    panelSettings.SetScreenToPanelSpaceFunction(null);
                }
            }
            ReleaseTexture();
            configured = false;
            panelSettings = null;
            originalTargetTexture = null;
        }

        private void ReleaseTexture()
        {
            if (uiTexture == null) return;
            if (panelSettings != null && ReferenceEquals(panelSettings.targetTexture, uiTexture))
                panelSettings.targetTexture = originalTargetTexture;
            uiTexture.Release();
            Destroy(uiTexture);
            uiTexture = null;
        }
    }
#endif
}
