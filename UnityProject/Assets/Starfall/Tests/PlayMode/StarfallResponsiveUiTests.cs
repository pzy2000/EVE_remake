using NUnit.Framework;
using Starfall.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.Tests.PlayMode
{
    public sealed class StarfallResponsiveUiTests
    {
        [TearDown]
        public void TearDown()
        {
            if (panelSettings) Object.Destroy(panelSettings);
        }

        private PanelSettings panelSettings;

        [Test]
        public void FoldableInnerScreen_ComesOutCompact()
        {
            // 2480x2200 physical at DPR 3 -> ~827x733 viewport; ScaleWithScreenSize
            // match 0.5 maps that to ~1532x1358 panel-space units at scale 1.
            panelSettings = MakeSettings(1f);
            Assert.That(StarfallResponsiveUi.Evaluate(1532f, 1358f, panelSettings), Is.True);
        }

        [Test]
        public void FoldableOuterScreen_ComesOutCompact()
        {
            // 2748x1172 physical at DPR 3 -> ~916x391 viewport; match 0.5 maps that
            // to ~2210x943 panel-space units: compact because of the height floor.
            panelSettings = MakeSettings(1f);
            Assert.That(StarfallResponsiveUi.Evaluate(2210f, 943f, panelSettings), Is.True);
        }

        [Test]
        public void DesktopReferenceResolution_KeepsFullLayout()
        {
            panelSettings = MakeSettings(1f);
            Assert.That(StarfallResponsiveUi.Evaluate(1920f, 1080f, panelSettings), Is.False);
        }

        [Test]
        public void UserScaleShiftsPanelSpace_ButNotTheReferenceBreakpoint()
        {
            // At 1.25x the same layout fills less panel space; the reference-space
            // verdict must stay identical so the slider cannot flip breakpoints.
            panelSettings = MakeSettings(1.25f);
            Assert.That(StarfallResponsiveUi.Evaluate(1532f / 1.25f, 1358f / 1.25f, panelSettings), Is.True);
            panelSettings.scale = 0.75f;
            Assert.That(StarfallResponsiveUi.Evaluate(1920f / 0.75f, 1080f / 0.75f, panelSettings), Is.False);
        }

        [Test]
        public void ConstantPixelSizeMode_JudgesRawViewportSize()
        {
            panelSettings = MakeSettings(1f);
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            Assert.That(StarfallResponsiveUi.Evaluate(827f, 733f, panelSettings), Is.True);
            Assert.That(StarfallResponsiveUi.Evaluate(1920f, 1080f, panelSettings), Is.False);
        }

        [Test]
        public void NonPositiveSizes_NeverTriggerCompact()
        {
            panelSettings = MakeSettings(1f);
            Assert.That(StarfallResponsiveUi.Evaluate(0f, 733f, panelSettings), Is.False);
            Assert.That(StarfallResponsiveUi.Evaluate(float.NaN, float.NaN, panelSettings), Is.False);
            Assert.That(StarfallResponsiveUi.Evaluate(1920f, 1080f, null), Is.False);
        }

        private static PanelSettings MakeSettings(float userScale)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.match = 0.5f;
            settings.scale = userScale;
            return settings;
        }
    }
}
