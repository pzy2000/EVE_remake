using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Starfall.Domain;
using Starfall.Persistence;

namespace Starfall.Tests.EditMode.Persistence
{
    public sealed class LegacyV1ImporterTests
    {
        private LegacyV1Importer _importer;
        private LegacyReferenceCatalog _references;

        [SetUp]
        public void SetUp()
        {
            _importer = new LegacyV1Importer();
            _references = new LegacyReferenceCatalog(
                FactionIds.All,
                ShipIds.All,
                ModuleIds.All,
                ItemIds.All,
                new[] { "sys_0", "sys_1" },
                new[] { "sys_0_st0", "sys_1_st0" },
                new[] { "agent_sys_0_st0_security" });
        }

        [Test]
        public void InspectAndConvert_MapsLegacyYToZAndPreservesSourceBytes()
        {
            var source = ValidLegacyBytes();
            var unchanged = source.ToArray();

            var inspection = _importer.Inspect(source, _references);
            var conversion = _importer.Convert(source, _references);

            Assert.That(inspection.IsValid, Is.True, inspection.ErrorMessage);
            Assert.That(inspection.ErrorCode, Is.EqualTo(LegacyImportErrorCode.None));
            Assert.That(inspection.SourceSha256, Is.EqualTo(Sha256(source)));
            Assert.That(inspection.PlayerName, Is.EqualTo("Legacy Pilot"));
            Assert.That(inspection.CurrentSystemId, Is.EqualTo("sys_0"));
            Assert.That(conversion.IsSuccess, Is.True);
            Assert.That(conversion.Envelope.SchemaVersion, Is.EqualTo(2));
            Assert.That(conversion.Envelope.GeneratorVersion, Is.EqualTo(1));
            Assert.That(conversion.Envelope.PlayerLocation.X, Is.EqualTo(123.5d));
            Assert.That(conversion.Envelope.PlayerLocation.Z, Is.EqualTo(-77.25d));
            Assert.That(conversion.Envelope.Player["location"], Is.Null);
            Assert.That(conversion.Envelope.LegacySourceSha256, Is.EqualTo(inspection.SourceSha256));
            CollectionAssert.AreEqual(unchanged, source, "Import must not mutate the source byte array.");
        }

        [Test]
        public void Convert_SameSourceIsIdempotent()
        {
            var source = ValidLegacyBytes();

            var first = _importer.Convert(source, _references);
            var second = _importer.Convert(source, _references);

            Assert.That(first.IsSuccess, Is.True, first.Inspection.ErrorMessage);
            Assert.That(second.IsSuccess, Is.True, second.Inspection.ErrorMessage);
            Assert.That(JsonConvert.SerializeObject(first.Envelope),
                Is.EqualTo(JsonConvert.SerializeObject(second.Envelope)));
            Assert.That(first.Envelope.LegacySourceSha256, Is.EqualTo(second.Envelope.LegacySourceSha256));
            Assert.That(first.Envelope.RngState, Is.EqualTo(second.Envelope.RngState));
            Assert.That(first.Envelope.NextEntityId, Is.EqualTo(second.Envelope.NextEntityId));
        }

        [Test]
        public void Inspect_RejectsUnknownStableReference()
        {
            var root = ValidLegacyRoot();
            root["player"]["ships"][0]["shipId"] = "unknown_hull";

            var result = _importer.Inspect(ToBytes(root), _references);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(LegacyImportErrorCode.UnknownReference));
        }

        [Test]
        public void Inspect_RejectsNonFiniteNumber()
        {
            var json = ValidLegacyRoot().ToString(Formatting.None)
                .Replace("\"time\":12.5", "\"time\":NaN");

            var result = _importer.Inspect(Encoding.UTF8.GetBytes(json), _references);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(LegacyImportErrorCode.NonFiniteNumber));
        }

        [Test]
        public void Inspect_RejectsWrongVersionAndCorruptJson()
        {
            var root = ValidLegacyRoot();
            root["version"] = 2;

            var wrongVersion = _importer.Inspect(ToBytes(root), _references);
            var corrupt = _importer.Inspect(Encoding.UTF8.GetBytes("{ definitely not json"), _references);

            Assert.That(wrongVersion.ErrorCode, Is.EqualTo(LegacyImportErrorCode.WrongVersion));
            Assert.That(corrupt.ErrorCode, Is.EqualTo(LegacyImportErrorCode.InvalidJson));
        }

        [Test]
        public void Inspect_RejectsFilesOverFiveMegabytesBeforeParsing()
        {
            var oversized = new byte[LegacyV1Importer.MaximumSourceBytes + 1];

            var result = _importer.Inspect(oversized, _references);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(LegacyImportErrorCode.FileTooLarge));
            Assert.That(result.SourceBytes, Is.EqualTo(oversized.Length));
        }

        private static byte[] ValidLegacyBytes()
        {
            return ToBytes(ValidLegacyRoot());
        }

        private static JObject ValidLegacyRoot()
        {
            return JObject.FromObject(new
            {
                version = 1,
                seed = 12345,
                time = 12.5,
                currentSystemId = "sys_0",
                player = new
                {
                    name = "Legacy Pilot",
                    empire = "aurelian",
                    credits = 50000,
                    lp = new { },
                    standings = new { aurelian = 1.0 },
                    ships = new[]
                    {
                        new
                        {
                            instId = "ship_start",
                            shipId = "acolyte",
                            name = "Acolyte",
                            hp = new { shield = 320, armor = 280, hull = 220 },
                            fitting = new
                            {
                                high = new string[] { "pulse_laser", "pulse_laser" },
                                mid = new string[] { "shield_booster", null },
                                low = new string[] { null, null },
                            },
                        },
                    },
                    activeShip = "ship_start",
                    cargo = new { ferrite = 12 },
                    hangar = new { mining_laser = 1 },
                    missions = Array.Empty<object>(),
                    missionCounts = new { },
                    location = new { systemId = "sys_0", dockedAt = "sys_0_st0", x = 123.5, y = -77.25 },
                    homeSystemId = "sys_0",
                    homeStationId = "sys_0_st0",
                    criminalTimer = 0,
                    destination = (string)null,
                    stats = new { kills = 0, missionsDone = 0, oreMined = 0, jumps = 0 },
                },
            });
        }

        private static byte[] ToBytes(JObject value)
        {
            return Encoding.UTF8.GetBytes(value.ToString(Formatting.None));
        }

        private static string Sha256(byte[] value)
        {
            using (var sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(value).Select(item => item.ToString("x2")));
            }
        }
    }
}
