using System;
using NUnit.Framework;
using Starfall.Domain;

namespace Starfall.Tests.EditMode.Core
{
    public sealed class RandomCompatibilityTests
    {
        [Test]
        public void Mulberry32_MatchesJavaScriptGoldenVector()
        {
            var expected = new[]
            {
                0.9797282677609473,
                0.3067522644996643,
                0.484205421525985,
                0.817934412509203,
                0.5094283693470061,
                0.34747186047025025,
                0.07375754183158278,
                0.7663964673411101,
                0.9968264393974096,
                0.8250224851071835,
            };
            var random = new Mulberry32(12345);
            foreach (var value in expected)
            {
                Assert.That(random.NextDouble(), Is.EqualTo(value));
            }
            Assert.That(random.State, Is.EqualTo(unchecked(12345u + 10u * 0x6D2B79F5u)));
        }

        [Test]
        public void Mulberry32_HelperCallOrder_IsStable()
        {
            var random = new Mulberry32(42);
            Assert.That(random.Range(-10, 10), Is.EqualTo(2.0220750384032726));
            Assert.That(random.RangeInclusive(2, 7), Is.EqualTo(4));
            Assert.That(random.Chance(0.9), Is.True);
            CollectionAssert.AreEqual(new[] { "d", "b", "a", "c" }, random.Shuffle(new[] { "a", "b", "c", "d" }));
        }

        [TestCase("", 2166136261u)]
        [TestCase("hello", 1335831723u)]
        [TestCase("STARFALL ODYSSEY", 2633061082u)]
        [TestCase("😀", 3409036472u)]
        [TestCase("𐐷", 1059832673u)]
        public void Fnv1a_HashesUtf16CodeUnitsLikeCharCodeAt(string value, uint expected)
        {
            Assert.That(Fnv1a.HashString(value), Is.EqualTo(expected));
        }

        [Test]
        public void JsRound_MatchesEcmaTieDirectionAndNegativeZero()
        {
            Assert.That(JsMath.Round(1.5), Is.EqualTo(2));
            Assert.That(JsMath.Round(-1.5), Is.EqualTo(-1));
            Assert.That(JsMath.Round(-1.6), Is.EqualTo(-2));
            Assert.That(BitConverter.DoubleToInt64Bits(JsMath.Round(-0.1)), Is.EqualTo(BitConverter.DoubleToInt64Bits(-0d)));
            Assert.That(double.IsNaN(JsMath.Round(double.NaN)), Is.True);
            Assert.That(JsMath.Round(double.PositiveInfinity), Is.EqualTo(double.PositiveInfinity));
        }
    }
}
