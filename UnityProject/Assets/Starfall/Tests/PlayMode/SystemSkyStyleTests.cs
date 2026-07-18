using NUnit.Framework;
using Starfall.Domain;
using Starfall.Presentation;
using UnityEngine;

namespace Starfall.Tests.PlayMode
{
    public sealed class SystemSkyStyleTests
    {
        [Test]
        public void EmpireSkyProfiles_UseDistinctFactionPalettesAndPatterns()
        {
            var aurelian = Style("sys_aurelian_00", FactionIds.Aurelian, new Color(0.83f, 0.69f, 0.22f));
            var kaldari = Style("sys_kaldari_00", FactionIds.Kaldari, new Color(0.29f, 0.62f, 0.87f));
            var meridian = Style("sys_meridian_00", FactionIds.Meridian, new Color(0.24f, 0.78f, 0.72f));
            var varkhald = Style("sys_varkhald_00", FactionIds.Varkhald, new Color(0.79f, 0.42f, 0.23f));

            Assert.That(ColorDistance(aurelian.NebulaA, kaldari.NebulaA), Is.GreaterThan(0.5f));
            Assert.That(ColorDistance(kaldari.NebulaA, meridian.NebulaA), Is.GreaterThan(0.35f));
            Assert.That(ColorDistance(meridian.NebulaA, varkhald.NebulaA), Is.GreaterThan(0.5f));
            Assert.That(new[] { aurelian.Pattern, kaldari.Pattern, meridian.Pattern, varkhald.Pattern },
                Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void SameSystemSky_IsDeterministic_ButSiblingSystemsVary()
        {
            var first = Style("sys_kaldari_03", FactionIds.Kaldari, Color.cyan);
            var repeated = Style("sys_kaldari_03", FactionIds.Kaldari, Color.cyan);
            var sibling = Style("sys_kaldari_04", FactionIds.Kaldari, Color.cyan);

            Assert.That(repeated.Seed, Is.EqualTo(first.Seed));
            Assert.That(repeated.RotationDegrees, Is.EqualTo(first.RotationDegrees));
            Assert.That(repeated.StarCount, Is.EqualTo(first.StarCount));
            Assert.That(sibling.Seed, Is.Not.EqualTo(first.Seed));
            Assert.That(sibling.RotationDegrees, Is.Not.EqualTo(first.RotationDegrees));
        }

        [Test]
        public void LowSecuritySystems_AreDarkerAndMoreNebulaDense()
        {
            var high = ProceduralSpaceMaterials.GetSystemSkyStyle(
                "sys_varkhald_border", FactionIds.Varkhald, Color.red, 0.9f);
            var low = ProceduralSpaceMaterials.GetSystemSkyStyle(
                "sys_varkhald_border", FactionIds.Varkhald, Color.red, 0.1f);

            Assert.That(low.Background.maxColorComponent, Is.LessThan(high.Background.maxColorComponent));
            Assert.That(low.NebulaStrength, Is.GreaterThan(high.NebulaStrength));
        }

        private static SystemSkyStyle Style(string systemId, string factionId, Color factionColor)
        {
            return ProceduralSpaceMaterials.GetSystemSkyStyle(systemId, factionId, factionColor, 0.7f);
        }

        private static float ColorDistance(Color first, Color second)
        {
            var delta = (Vector4)first - (Vector4)second;
            return delta.magnitude;
        }
    }
}
