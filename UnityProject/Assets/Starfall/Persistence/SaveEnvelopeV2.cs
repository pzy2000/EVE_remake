using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Starfall.Domain;

namespace Starfall.Persistence
{
    public sealed class PlayerLocationV2
    {
        public PlayerLocationV2()
        {
        }

        public PlayerLocationV2(string systemId, string dockedAt, double x, double z)
        {
            SystemId = systemId;
            DockedAt = dockedAt;
            X = x;
            Z = z;
        }

        [JsonProperty("systemId", Required = Required.Always)]
        public string SystemId { get; set; }

        [JsonProperty("dockedAt", NullValueHandling = NullValueHandling.Include)]
        public string DockedAt { get; set; }

        [JsonProperty("x", Required = Required.Always)]
        public double X { get; set; }

        [JsonProperty("z", Required = Required.Always)]
        public double Z { get; set; }

        [JsonIgnore]
        public SimVec2 Position => new SimVec2(X, Z);
    }

    public sealed class SaveEnvelopeV2
    {
        public const int CurrentSchemaVersion = 2;
        public const int CurrentGeneratorVersion = 1;

        public SaveEnvelopeV2()
        {
            SchemaVersion = CurrentSchemaVersion;
            GeneratorVersion = CurrentGeneratorVersion;
            Player = new JObject();
        }

        [JsonProperty("schemaVersion", Order = 1, Required = Required.Always)]
        public int SchemaVersion { get; set; }

        [JsonProperty("generatorVersion", Order = 2, Required = Required.Always)]
        public int GeneratorVersion { get; set; }

        [JsonProperty("seed", Order = 3, Required = Required.Always)]
        public uint Seed { get; set; }

        [JsonProperty("simulationTime", Order = 4, Required = Required.Always)]
        public double SimulationTime { get; set; }

        [JsonProperty("playerLocation", Order = 5, Required = Required.Always)]
        public PlayerLocationV2 PlayerLocation { get; set; }

        /// <summary>
        /// Versioned player payload. Keeping it as JSON until the complete
        /// simulation state is available preserves legacy ships, fitting,
        /// inventory, standings, and mission fields without coupling storage to
        /// Unity views.
        /// </summary>
        [JsonProperty("player", Order = 6, Required = Required.Always)]
        public JObject Player { get; set; }

        [JsonProperty("rngState", Order = 7, Required = Required.Always)]
        public uint RngState { get; set; }

        [JsonProperty("nextEntityId", Order = 8, Required = Required.Always)]
        public ulong NextEntityId { get; set; }

        [JsonProperty("legacySourceSha256", Order = 9, NullValueHandling = NullValueHandling.Ignore)]
        public string LegacySourceSha256 { get; set; }

        public void Validate()
        {
            if (SchemaVersion != CurrentSchemaVersion)
            {
                throw new InvalidOperationException($"Unsupported save schema {SchemaVersion}; expected {CurrentSchemaVersion}.");
            }

            if (GeneratorVersion != CurrentGeneratorVersion)
            {
                throw new InvalidOperationException($"Unsupported universe generator {GeneratorVersion}; expected {CurrentGeneratorVersion}.");
            }

            if (!IsFinite(SimulationTime) || SimulationTime < 0d)
            {
                throw new InvalidOperationException("Simulation time must be a finite, non-negative number.");
            }

            if (PlayerLocation == null || string.IsNullOrWhiteSpace(PlayerLocation.SystemId))
            {
                throw new InvalidOperationException("Player location and system ID are required.");
            }

            if (!IsFinite(PlayerLocation.X) || !IsFinite(PlayerLocation.Z))
            {
                throw new InvalidOperationException("Player coordinates must be finite numbers.");
            }

            if (Player == null)
            {
                throw new InvalidOperationException("Player payload is required.");
            }

            ValidateFiniteNumbers(Player);

            if (!string.IsNullOrEmpty(LegacySourceSha256) && !IsLowerHexSha256(LegacySourceSha256))
            {
                throw new InvalidOperationException("Legacy source SHA-256 must be 64 lowercase hexadecimal characters.");
            }
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static void ValidateFiniteNumbers(JToken token)
        {
            foreach (var value in EnumerateTokens(token))
            {
                if (value.Type != JTokenType.Float)
                {
                    continue;
                }

                var number = Convert.ToDouble(((JValue)value).Value, CultureInfo.InvariantCulture);
                if (!IsFinite(number))
                {
                    throw new InvalidOperationException("Save data contains a non-finite number.");
                }
            }
        }

        internal static IEnumerable<JToken> EnumerateTokens(JToken root)
        {
            if (root == null)
            {
                yield break;
            }

            var pending = new Stack<JToken>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                yield return current;
                if (!(current is JContainer container))
                {
                    continue;
                }

                var children = new List<JToken>(container.Children());
                for (var i = children.Count - 1; i >= 0; i--)
                {
                    pending.Push(children[i]);
                }
            }
        }

        private static bool IsLowerHexSha256(string value)
        {
            if (value.Length != 64)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
