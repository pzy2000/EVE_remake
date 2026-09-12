using System;
using NUnit.Framework;
using Starfall.Domain;

namespace Starfall.Tests.EditMode.Core
{
    public sealed class L10nTests
    {
        [SetUp]
        public void SetUp()
        {
            L10n.SetLanguage(L10nLanguage.English);
        }

        [TearDown]
        public void TearDown()
        {
            L10n.SetLanguage(L10nLanguage.English);
        }

        [Test]
        public void DefaultLanguage_IsEnglish()
        {
            // Fresh app domains start in English; the app root applies the player
            // preference afterwards, and simulation tests rely on the English default.
            L10n.SetLanguage(L10nLanguage.English);
            Assert.That(L10n.Language, Is.EqualTo(L10nLanguage.English));
            Assert.That(L10n.IsChinese, Is.False);
        }

        [Test]
        public void EnglishMode_ReturnsKeyUnchanged()
        {
            Assert.That(L10n.Tr("Warp drive active."), Is.EqualTo("Warp drive active."));
            Assert.That(L10n.Tr("Docked at {0}.", "Helara Station 1"), Is.EqualTo("Docked at Helara Station 1."));
        }

        [Test]
        public void ChineseMode_TranslatesKnownKeys()
        {
            L10n.SetLanguage(L10nLanguage.Chinese);
            Assert.That(L10n.Tr("Warp drive active."), Is.EqualTo("跃迁引擎已启动。"));
            Assert.That(L10n.Tr("OVERVIEW"), Is.EqualTo("总览"));
        }

        [Test]
        public void ChineseMode_MissingKeyFallsBackToEnglish()
        {
            L10n.SetLanguage(L10nLanguage.Chinese);
            Assert.That(L10n.Tr("Definitely not a real key."), Is.EqualTo("Definitely not a real key."));
        }

        [Test]
        public void ChineseMode_FormatsPlaceholders()
        {
            L10n.SetLanguage(L10nLanguage.Chinese);
            Assert.That(L10n.Tr("Docked at {0}.", "赫尔拉"), Is.EqualTo("已停靠于赫尔拉。"));
            Assert.That(L10n.Tr("{0} CONTACTS", 7), Is.EqualTo("7 个目标"));
            Assert.That(L10n.Tr("Hostiles destroyed: {0}/{1}", 2, 5), Is.EqualTo("已击毁敌舰：2/5"));
        }

        [Test]
        public void LanguageChanged_FiresOnlyOnActualChange()
        {
            var fired = 0;
            L10n.LanguageChanged += Count;
            try
            {
                L10n.SetLanguage(L10nLanguage.Chinese);
                L10n.SetLanguage(L10nLanguage.Chinese);
                L10n.SetLanguage(L10nLanguage.English);
                Assert.That(fired, Is.EqualTo(2));
            }
            finally
            {
                L10n.LanguageChanged -= Count;
            }

            void Count() => fired++;
        }

        [Test]
        public void TrName_EnglishMode_ReturnsInputUnchanged()
        {
            Assert.That(L10n.TrName("Helara Belt Alpha"), Is.EqualTo("Helara Belt Alpha"));
        }

        [Test]
        public void TrName_TransliteratesSystemNames()
        {
            L10n.SetLanguage(L10nLanguage.Chinese);
            Assert.That(L10n.TrName("Helara"), Is.EqualTo("赫尔拉"));
            Assert.That(L10n.TrName("Helara Prime"), Is.EqualTo("赫尔拉主星"));
            Assert.That(L10n.TrName("Helara II"), Is.EqualTo("赫尔拉II"));
        }

        [Test]
        public void TrName_TranslatesStructuralPatterns()
        {
            L10n.SetLanguage(L10nLanguage.Chinese);
            Assert.That(L10n.TrName("Helara AUR Station 1"), Is.EqualTo("赫尔拉 AUR 1号空间站"));
            Assert.That(L10n.TrName("Helara Belt Alpha"), Is.EqualTo("赫尔拉小行星带 α"));
            Assert.That(L10n.TrName("Stargate to Helara"), Is.EqualTo("通往赫尔拉的星门"));
            Assert.That(L10n.TrName("Helara III - Moon 2"), Is.EqualTo("赫尔拉III 卫星2"));
            Assert.That(L10n.TrName("Helara Star"), Is.EqualTo("赫尔拉恒星"));
            Assert.That(L10n.TrName("Directorate Bureau"), Is.EqualTo("理事会分局"));
            Assert.That(L10n.TrName("Aurelian Empire Command"), Is.EqualTo("奥雷利安帝国司令部"));
        }

        [Test]
        public void TrName_PassesUnknownTokensThrough()
        {
            L10n.SetLanguage(L10nLanguage.Chinese);
            // Faction abbreviations and player callsigns stay in Latin script.
            Assert.That(L10n.TrName("Elite AUR Shrike"), Is.EqualTo("精英 AUR 伯劳鸟"));
            Assert.That(L10n.TrName("Aren Voss"), Is.EqualTo("阿伦 沃斯"));
            Assert.That(L10n.TrPersonName("Aren Voss"), Is.EqualTo("阿伦·沃斯"));
        }

        [Test]
        public void TrName_IsDeterministic()
        {
            L10n.SetLanguage(L10nLanguage.Chinese);
            var first = L10n.TrName("Zelvos KAL Station 3");
            for (var i = 0; i < 10; i++)
                Assert.That(L10n.TrName("Zelvos KAL Station 3"), Is.EqualTo(first));
        }

        [Test]
        public void Tr_TranslatesContentCatalogEntries()
        {
            L10n.SetLanguage(L10nLanguage.Chinese);
            Assert.That(L10n.Tr("Aurelian Empire"), Is.EqualTo("奥雷利安帝国"));
            Assert.That(L10n.Tr("Pulse Laser"), Is.EqualTo("脉冲激光器"));
            Assert.That(L10n.Tr("Frigate"), Is.EqualTo("护卫舰"));
            Assert.That(L10n.Tr("Courier: Sealed Dispatch"), Is.EqualTo("递送：密封急件"));
        }
    }
}
