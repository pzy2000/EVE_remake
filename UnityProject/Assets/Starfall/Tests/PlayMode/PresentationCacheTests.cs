using NUnit.Framework;
using Starfall.Presentation;
using UnityEngine;

namespace Starfall.Tests.PlayMode
{
    /// <summary>
    /// Guards the procedural asset caches: texture eviction must stay in sync
    /// with the material cache or revisited systems render untextured blobs.
    /// </summary>
    public sealed class PresentationCacheTests
    {
        private const int MaxCachedTextures = 32;

        [Test]
        public void PlanetTextureEviction_EvictsTheOwningMaterialToo()
        {
            var color = new Color(0.5f, 0.5f, 0.5f);
            for (var i = 0; i < MaxCachedTextures + 8; i++)
            {
                var material = ProceduralSpaceMaterials.GetPlanetMaterial($"eviction-test-{i}", color, moon: false);
                Assert.That(material, Is.Not.Null);
            }

            Assert.That(ProceduralSpaceMaterials.IsPlanetCached($"eviction-test-{MaxCachedTextures + 7}", color, false),
                Is.True, "The newest planet must stay cached.");
            Assert.That(ProceduralSpaceMaterials.IsPlanetCached("eviction-test-0", color, false), Is.False,
                "Evicting a planet texture must evict its material: a cached material " +
                "whose _BaseMap was destroyed renders as a flat blob on revisit.");
        }

        [Test]
        public void EvictedPlanets_RebuildCleanlyOnRevisit()
        {
            var color = new Color(0.2f, 0.6f, 0.9f);
            var first = ProceduralSpaceMaterials.GetPlanetMaterial("revisit-test-0", color, moon: false);
            // Push well past the cap so the entry is evicted regardless of how
            // many entries earlier tests left behind in the static caches.
            for (var i = 1; i < MaxCachedTextures * 2 + 16; i++)
                ProceduralSpaceMaterials.GetPlanetMaterial($"revisit-test-{i}", color, moon: false);
            Assert.That(ProceduralSpaceMaterials.IsPlanetCached("revisit-test-0", color, false), Is.False,
                "Sanity: the entry must actually have been evicted by the flood.");

            var rebuilt = ProceduralSpaceMaterials.GetPlanetMaterial("revisit-test-0", color, moon: false);
            Assert.That(rebuilt, Is.Not.Null);
            Assert.That(rebuilt, Is.Not.SameAs(first), "The evicted material must be rebuilt, not resurrected.");
            Assert.That(rebuilt.mainTexture, Is.Not.Null,
                "The rebuilt material must carry a fresh texture, not the destroyed one.");
        }
    }
}
