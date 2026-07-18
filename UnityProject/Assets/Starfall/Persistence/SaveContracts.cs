using System;
using System.Collections.Generic;

namespace Starfall.Persistence
{
    public enum SaveSlot
    {
        Auto = 0,
        Slot1 = 1,
        Slot2 = 2,
        Slot3 = 3,
    }

    public static class SaveSlots
    {
        public static readonly IReadOnlyList<SaveSlot> All = Array.AsReadOnly(new[]
        {
            SaveSlot.Auto,
            SaveSlot.Slot1,
            SaveSlot.Slot2,
            SaveSlot.Slot3,
        });

        public static string FileStem(this SaveSlot slot)
        {
            switch (slot)
            {
                case SaveSlot.Auto: return "auto";
                case SaveSlot.Slot1: return "slot1";
                case SaveSlot.Slot2: return "slot2";
                case SaveSlot.Slot3: return "slot3";
                default: throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown save slot.");
            }
        }
    }

    public sealed class SaveSlotInfo
    {
        public SaveSlotInfo(SaveSlot slot, bool exists, bool isCorrupt, bool recoveredFromBackup,
            string playerName, long? credits, string currentSystemId, double? simulationTime,
            DateTime? lastWriteTimeUtc)
        {
            Slot = slot;
            Exists = exists;
            IsCorrupt = isCorrupt;
            RecoveredFromBackup = recoveredFromBackup;
            PlayerName = playerName;
            Credits = credits;
            CurrentSystemId = currentSystemId;
            SimulationTime = simulationTime;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        public SaveSlot Slot { get; }
        public bool Exists { get; }
        public bool IsCorrupt { get; }
        public bool RecoveredFromBackup { get; }
        public string PlayerName { get; }
        public long? Credits { get; }
        public string CurrentSystemId { get; }
        public double? SimulationTime { get; }
        public DateTime? LastWriteTimeUtc { get; }
    }

    public interface ISaveService
    {
        IReadOnlyList<SaveSlotInfo> List();
        void Save(SaveSlot slot, SaveEnvelopeV2 envelope);
        SaveEnvelopeV2 Load(SaveSlot slot);
    }
}
