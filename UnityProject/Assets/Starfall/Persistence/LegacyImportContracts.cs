using System;
using System.Collections.Generic;
using Starfall.Domain;

namespace Starfall.Persistence
{
    public enum LegacyImportErrorCode
    {
        None = 0,
        EmptyInput,
        FileTooLarge,
        InvalidUtf8,
        InvalidJson,
        WrongVersion,
        MissingField,
        InvalidValue,
        NonFiniteNumber,
        UnknownReference,
        DuplicateId,
    }

    public sealed class LegacyImportInspection
    {
        internal LegacyImportInspection(bool isValid, LegacyImportErrorCode errorCode, string errorMessage,
            int sourceBytes, string sourceSha256, string playerName, string empireId, long? credits,
            string currentSystemId, double? simulationTime)
        {
            IsValid = isValid;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            SourceBytes = sourceBytes;
            SourceSha256 = sourceSha256;
            PlayerName = playerName;
            EmpireId = empireId;
            Credits = credits;
            CurrentSystemId = currentSystemId;
            SimulationTime = simulationTime;
        }

        public bool IsValid { get; }
        public LegacyImportErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public int SourceBytes { get; }
        public string SourceSha256 { get; }
        public string PlayerName { get; }
        public string EmpireId { get; }
        public long? Credits { get; }
        public string CurrentSystemId { get; }
        public double? SimulationTime { get; }
    }

    public sealed class LegacyConversionResult
    {
        internal LegacyConversionResult(LegacyImportInspection inspection, SaveEnvelopeV2 envelope)
        {
            Inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
            Envelope = envelope;
        }

        public bool IsSuccess => Inspection.IsValid && Envelope != null;
        public LegacyImportInspection Inspection { get; }
        public SaveEnvelopeV2 Envelope { get; }
    }

    public interface ILegacyV1Importer
    {
        LegacyImportInspection Inspect(byte[] sourceBytes, LegacyReferenceCatalog references);
        LegacyConversionResult Convert(byte[] sourceBytes, LegacyReferenceCatalog references);
    }

    /// <summary>
    /// Stable IDs accepted by the legacy importer. Universe IDs are supplied by
    /// the seed-specific generated universe; content IDs can be sourced from an
    /// IContentCatalog.
    /// </summary>
    public sealed class LegacyReferenceCatalog
    {
        private readonly HashSet<string> _factions;
        private readonly HashSet<string> _ships;
        private readonly HashSet<string> _modules;
        private readonly HashSet<string> _items;
        private readonly HashSet<string> _systems;
        private readonly HashSet<string> _stations;
        private readonly HashSet<string> _agents;

        public LegacyReferenceCatalog(IEnumerable<string> factionIds, IEnumerable<string> shipIds,
            IEnumerable<string> moduleIds, IEnumerable<string> itemIds, IEnumerable<string> systemIds,
            IEnumerable<string> stationIds, IEnumerable<string> agentIds)
        {
            _factions = Copy(factionIds, nameof(factionIds));
            _ships = Copy(shipIds, nameof(shipIds));
            _modules = Copy(moduleIds, nameof(moduleIds));
            _items = Copy(itemIds, nameof(itemIds));
            _systems = Copy(systemIds, nameof(systemIds));
            _stations = Copy(stationIds, nameof(stationIds));
            _agents = Copy(agentIds, nameof(agentIds));
        }

        public static LegacyReferenceCatalog FromContent(IContentCatalog content,
            IEnumerable<string> systemIds, IEnumerable<string> stationIds, IEnumerable<string> agentIds)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            return new LegacyReferenceCatalog(content.Factions.Keys, content.Ships.Keys, content.Modules.Keys,
                content.Items.Keys, systemIds, stationIds, agentIds);
        }

        internal bool HasFaction(string id) => _factions.Contains(id);
        internal bool HasShip(string id) => _ships.Contains(id);
        internal bool HasModule(string id) => _modules.Contains(id);
        internal bool HasItem(string id) => _items.Contains(id);
        internal bool HasSystem(string id) => _systems.Contains(id);
        internal bool HasStation(string id) => _stations.Contains(id);
        internal bool HasAgent(string id) => _agents.Contains(id);

        private static HashSet<string> Copy(IEnumerable<string> values, string parameterName)
        {
            if (values == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException("Reference IDs cannot be null or blank.", parameterName);
                }

                result.Add(value);
            }

            return result;
        }
    }
}
