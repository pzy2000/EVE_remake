using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Starfall.Persistence;

namespace Starfall.Tests.EditMode.Persistence
{
    public sealed class FileSaveServiceTests
    {
        private string _temporaryDirectory;
        private FileSaveService _service;

        [SetUp]
        public void SetUp()
        {
            _temporaryDirectory = Path.Combine(Path.GetTempPath(), "starfall-save-tests", Guid.NewGuid().ToString("N"));
            _service = new FileSaveService(_temporaryDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryDirectory))
            {
                Directory.Delete(_temporaryDirectory, true);
            }
        }

        [TestCase(SaveSlot.Auto)]
        [TestCase(SaveSlot.Slot1)]
        [TestCase(SaveSlot.Slot2)]
        [TestCase(SaveSlot.Slot3)]
        public void SaveLoad_RoundTripsEverySupportedSlot(SaveSlot slot)
        {
            var expected = Envelope("Pilot One", 7654321, 125.5d);

            _service.Save(slot, expected);
            var actual = _service.Load(slot);

            Assert.That(actual.SchemaVersion, Is.EqualTo(2));
            Assert.That(actual.GeneratorVersion, Is.EqualTo(1));
            Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
            Assert.That(actual.SimulationTime, Is.EqualTo(expected.SimulationTime));
            Assert.That(actual.PlayerLocation.SystemId, Is.EqualTo("sys_0"));
            Assert.That(actual.PlayerLocation.X, Is.EqualTo(123.25d));
            Assert.That(actual.PlayerLocation.Z, Is.EqualTo(-456.75d));
            Assert.That(actual.RngState, Is.EqualTo(expected.RngState));
            Assert.That(actual.NextEntityId, Is.EqualTo(expected.NextEntityId));
            Assert.That(actual.Player.ToString(Formatting.None), Is.EqualTo(expected.Player.ToString(Formatting.None)));
            Assert.That(File.Exists(_service.GetSlotPath(slot)), Is.True);
            Assert.That(File.Exists(_service.GetTemporaryPath(slot)), Is.False);
        }

        [Test]
        public void List_ReturnsAllSlotsAndReadableMetadata()
        {
            _service.Save(SaveSlot.Slot2, Envelope("Metadata Pilot", 987654, 44d));

            var slots = _service.List();

            Assert.That(slots, Has.Count.EqualTo(4));
            var slot = slots[(int)SaveSlot.Slot2];
            Assert.That(slot.Slot, Is.EqualTo(SaveSlot.Slot2));
            Assert.That(slot.Exists, Is.True);
            Assert.That(slot.IsCorrupt, Is.False);
            Assert.That(slot.RecoveredFromBackup, Is.False);
            Assert.That(slot.PlayerName, Is.EqualTo("Metadata Pilot"));
            Assert.That(slot.Credits, Is.EqualTo(987654));
            Assert.That(slot.CurrentSystemId, Is.EqualTo("sys_0"));
            Assert.That(slot.SimulationTime, Is.EqualTo(44d));
            Assert.That(slot.LastWriteTimeUtc, Is.Not.Null);
        }

        [Test]
        public void Load_CorruptPrimaryRollsBackToPreviousBackup()
        {
            var first = Envelope("Before", 100, 10d);
            var second = Envelope("After", 200, 20d);
            _service.Save(SaveSlot.Slot1, first);
            _service.Save(SaveSlot.Slot1, second);
            Assert.That(File.Exists(_service.GetBackupPath(SaveSlot.Slot1)), Is.True);

            File.WriteAllText(_service.GetSlotPath(SaveSlot.Slot1), "{ corrupt", new System.Text.UTF8Encoding(false));
            var recovered = _service.Load(SaveSlot.Slot1);
            var info = _service.List()[(int)SaveSlot.Slot1];

            Assert.That(recovered.Player.Value<string>("name"), Is.EqualTo("Before"));
            Assert.That(recovered.SimulationTime, Is.EqualTo(10d));
            Assert.That(info.Exists, Is.True);
            Assert.That(info.IsCorrupt, Is.False);
            Assert.That(info.RecoveredFromBackup, Is.True);
        }

        [Test]
        public void Save_WhenTemporaryWriteFails_KeepsPreviousPrimary()
        {
            _service.Save(SaveSlot.Auto, Envelope("Stable", 100, 10d));
            Directory.CreateDirectory(_service.GetTemporaryPath(SaveSlot.Auto));

            var exception = Assert.Catch<Exception>(
                () => _service.Save(SaveSlot.Auto, Envelope("Must Not Replace", 999, 99d)));
            Assert.That(exception, Is.TypeOf<IOException>().Or.TypeOf<UnauthorizedAccessException>());

            var loaded = _service.Load(SaveSlot.Auto);
            Assert.That(loaded.Player.Value<string>("name"), Is.EqualTo("Stable"));
            Assert.That(loaded.SimulationTime, Is.EqualTo(10d));
        }

        [Test]
        public void Load_WhenPrimaryAndBackupAreCorrupt_ThrowsInvalidData()
        {
            _service.Save(SaveSlot.Slot3, Envelope("Before", 100, 10d));
            _service.Save(SaveSlot.Slot3, Envelope("After", 200, 20d));
            File.WriteAllText(_service.GetSlotPath(SaveSlot.Slot3), "bad primary");
            File.WriteAllText(_service.GetBackupPath(SaveSlot.Slot3), "bad backup");

            Assert.That(() => _service.Load(SaveSlot.Slot3), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Save_RejectsNonFiniteStateBeforeTouchingDisk()
        {
            var envelope = Envelope("Pilot", 1, double.NaN);

            Assert.That(() => _service.Save(SaveSlot.Auto, envelope), Throws.TypeOf<InvalidOperationException>());
            Assert.That(Directory.Exists(_temporaryDirectory), Is.False);
        }

        private static SaveEnvelopeV2 Envelope(string playerName, long credits, double simulationTime)
        {
            return new SaveEnvelopeV2
            {
                Seed = 12345,
                SimulationTime = simulationTime,
                PlayerLocation = new PlayerLocationV2("sys_0", "sys_0_st0", 123.25d, -456.75d),
                Player = JObject.FromObject(new
                {
                    name = playerName,
                    empire = "aurelian",
                    credits,
                    cargo = new { ferrite = 12 },
                }),
                RngState = 0x1234abcd,
                NextEntityId = 77,
            };
        }
    }
}
