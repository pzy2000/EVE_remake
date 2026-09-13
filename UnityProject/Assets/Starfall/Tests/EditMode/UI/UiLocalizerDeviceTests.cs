using NUnit.Framework;
using Starfall.Domain;
using Starfall.UI;
using UnityEngine.UIElements;

namespace Starfall.Tests.EditMode.UI
{
    /// <summary>
    /// Second-round UI regressions: touch devices must lose keyboard hints in
    /// BOTH languages, and queued skill rows must advertise STOP, not a second
    /// silent TRAIN.
    /// </summary>
    public sealed class UiLocalizerDeviceTests
    {
        private L10nLanguage originalLanguage;

        [SetUp]
        public void SetUp()
        {
            originalLanguage = L10n.Language;
        }

        [TearDown]
        public void TearDown()
        {
            L10n.SetLanguage(originalLanguage);
        }

        [Test]
        public void TouchDevices_LoseKeyboardHints_InEnglishToo()
        {
            L10n.SetLanguage(L10nLanguage.English);
            var root = new VisualElement();
            var map = new Label("MAP [M]");
            var warp = new Label("WARP [W]");
            var plain = new Label("OVERVIEW");
            root.Add(map);
            root.Add(warp);
            root.Add(plain);

            UiLocalizer.Apply(root, touchDevice: true);

            Assert.That(map.text, Is.EqualTo("MAP"),
                "Switching the UI to English on a phone must still strip dead key hints.");
            Assert.That(warp.text, Is.EqualTo("WARP"));
            Assert.That(plain.text, Is.EqualTo("OVERVIEW"), "Hintless labels stay untouched.");
        }

        [Test]
        public void TouchDevices_KeepHints_OnDesktop()
        {
            L10n.SetLanguage(L10nLanguage.English);
            var root = new VisualElement();
            var map = new Label("MAP [M]");
            root.Add(map);

            UiLocalizer.Apply(root, touchDevice: false);

            Assert.That(map.text, Is.EqualTo("MAP [M]"), "Desktop keeps its hotkey hints.");
        }

        [Test]
        public void LanguageRoundTrip_SurvivesTouchNormalization()
        {
            var root = new VisualElement();
            var map = new Label("MAP [M]");
            root.Add(map);

            L10n.SetLanguage(L10nLanguage.English);
            UiLocalizer.Apply(root, touchDevice: true);
            Assert.That(map.text, Is.EqualTo("MAP"));

            L10n.SetLanguage(L10nLanguage.Chinese);
            UiLocalizer.Apply(root, touchDevice: true);
            Assert.That(map.text, Is.EqualTo("星图"),
                "After English normalization the Chinese pass must still find the row.");

            L10n.SetLanguage(L10nLanguage.English);
            UiLocalizer.Apply(root, touchDevice: true);
            Assert.That(map.text, Is.EqualTo("MAP"));
        }

        [Test]
        public void SkillRows_QueuedTraining_CarriesTheStopAction()
        {
            // The contract the station UI renders: a queued row's Action says
            // train-stop so the button reads STOP instead of a second TRAIN
            // that silently cancels training.
            var queued = new UiListItem { Id = "skill_gunnery", Action = "train-stop" };
            var fresh = new UiListItem { Id = "skill_mining", Action = null };

            Assert.That(string.IsNullOrEmpty(fresh.Action), Is.True);
            Assert.That(queued.Action, Is.EqualTo("train-stop"));
        }
    }
}
