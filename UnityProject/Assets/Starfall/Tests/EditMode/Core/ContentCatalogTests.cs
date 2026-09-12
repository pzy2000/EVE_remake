using System.Linq;
using NUnit.Framework;
using Starfall.Content;
using Starfall.Domain;

namespace Starfall.Tests.EditMode.Core
{
    public sealed class ContentCatalogTests
    {
        private GameContentCatalog _catalog;

        [SetUp]
        public void SetUp() => _catalog = new GameContentCatalog();

        [Test]
        public void Catalog_HasEveryLegacyStableId()
        {
            Assert.That(_catalog.Factions.Count, Is.EqualTo(10));
            Assert.That(_catalog.Ships.Count, Is.EqualTo(22));
            Assert.That(_catalog.Modules.Count, Is.EqualTo(14));
            Assert.That(_catalog.Items.Count, Is.EqualTo(4));
            CollectionAssert.AreEquivalent(FactionIds.All, _catalog.Factions.Keys);
            CollectionAssert.AreEquivalent(ShipIds.All, _catalog.Ships.Keys);
            CollectionAssert.AreEquivalent(ModuleIds.All, _catalog.Modules.Keys);
            CollectionAssert.AreEquivalent(ItemIds.All, _catalog.Items.Keys);
        }

        [Test]
        public void ShipValues_MatchLegacyCatalog()
        {
            var acolyte = _catalog.Ships[ShipIds.Acolyte];
            Assert.That(acolyte.FactionId, Is.EqualTo(FactionIds.Aurelian));
            Assert.That(acolyte.Class, Is.EqualTo(ShipClass.Frigate));
            Assert.That(acolyte.Speed, Is.EqualTo(150));
            Assert.That(acolyte.WarpSpeed, Is.EqualTo(700));
            Assert.That(acolyte.HitPoints.Shield, Is.EqualTo(320));
            Assert.That(acolyte.HitPoints.Armor, Is.EqualTo(280));
            Assert.That(acolyte.HitPoints.Hull, Is.EqualTo(220));
            Assert.That(acolyte.Slots.High, Is.EqualTo(2));
            Assert.That(acolyte.Slots.Mid, Is.EqualTo(2));
            Assert.That(acolyte.Slots.Low, Is.EqualTo(2));
            Assert.That(acolyte.CargoCapacity, Is.EqualTo(160));
            Assert.That(acolyte.LockRange, Is.EqualTo(320));
            Assert.That(acolyte.Price, Is.EqualTo(40000));

            var enforcer = _catalog.Ships[ShipIds.Enforcer];
            Assert.That(enforcer.NpcOnly, Is.True);
            Assert.That(enforcer.Price, Is.Zero);
            Assert.That(enforcer.HitPoints.Shield, Is.EqualTo(3000));
            Assert.That(enforcer.LockRange, Is.EqualTo(600));
            Assert.That(_catalog.Ships.Values.Count(x => x.NpcOnly), Is.EqualTo(1));
        }

        [Test]
        public void ModuleAndItemValues_MatchLegacyCatalog()
        {
            var missiles = _catalog.Modules[ModuleIds.MissileLauncher];
            Assert.That(missiles.Kind, Is.EqualTo(ModuleKind.Weapon));
            Assert.That(missiles.Damage, Is.EqualTo(30));
            Assert.That(missiles.CycleTime, Is.EqualTo(4));
            Assert.That(missiles.Range, Is.EqualTo(170));
            Assert.That(missiles.Projectile, Is.True);

            var extender = _catalog.Modules[ModuleIds.ShieldExtender];
            Assert.That(extender.ShieldBonus, Is.EqualTo(200));
            Assert.That(_catalog.Modules[ModuleIds.DamageAmp].DamageMultiplier, Is.EqualTo(1.18));
            Assert.That(_catalog.Modules[ModuleIds.CargoExpander].CargoBonus, Is.EqualTo(250));

            Assert.That(_catalog.Items[ItemIds.Novacite].Volume, Is.EqualTo(1.5));
            Assert.That(_catalog.Items[ItemIds.Crystalline].BasePrice, Is.EqualTo(140));
            Assert.That(_catalog.Items[ItemIds.SealedCargo].NoMarket, Is.True);
        }

        [Test]
        public void StandingMatrix_IsCompleteSymmetricAndExact()
        {
            foreach (var first in FactionIds.All)
            foreach (var second in FactionIds.All)
            {
                Assert.That(_catalog.FactionRelation(first, second),
                    Is.EqualTo(_catalog.FactionRelation(second, first)), $"{first} <-> {second}");
            }

            Assert.That(_catalog.FactionRelation(FactionIds.Directorate, FactionIds.BloodReavers), Is.EqualTo(-10));
            Assert.That(_catalog.FactionRelation(FactionIds.Aurelian, FactionIds.Kaldari), Is.EqualTo(5));
            Assert.That(_catalog.FactionRelation(FactionIds.Meridian, FactionIds.CrimsonHand), Is.EqualTo(-8));
            Assert.That(_catalog.FactionRelation("unknown", FactionIds.Aurelian), Is.Zero);
        }
    }
}
