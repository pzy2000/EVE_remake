using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static Starfall.Domain.L10n;

namespace Starfall.Persistence
{
    public sealed class LegacyV1Importer : ILegacyV1Importer
    {
        public const int MaximumSourceBytes = 5 * 1024 * 1024;
        private const double SimulationStepSeconds = 0.05d;
        private const double UInt32Range = 4294967296d;
        private const uint MulberryIncrement = 0x6D2B79F5u;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public LegacyImportInspection Inspect(byte[] sourceBytes, LegacyReferenceCatalog references)
        {
            if (sourceBytes == null)
            {
                throw new ArgumentNullException(nameof(sourceBytes));
            }

            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }

            if (sourceBytes.Length == 0)
            {
                return Failure(LegacyImportErrorCode.EmptyInput, Tr("Legacy save is empty."), sourceBytes, null);
            }

            if (sourceBytes.Length > MaximumSourceBytes)
            {
                return Failure(LegacyImportErrorCode.FileTooLarge,
                    Tr("Legacy save exceeds the {0} byte limit.", MaximumSourceBytes), sourceBytes, null);
            }

            var sha256 = ComputeSha256(sourceBytes);
            try
            {
                var root = Parse(sourceBytes);
                Validate(root, references);
                var player = (JObject)root["player"];
                var location = (JObject)player["location"];
                return new LegacyImportInspection(true, LegacyImportErrorCode.None, null, sourceBytes.Length, sha256,
                    player.Value<string>("name"), player.Value<string>("empire"), player.Value<long?>("credits"),
                    location.Value<string>("systemId"), root.Value<double>("time"));
            }
            catch (DecoderFallbackException exception)
            {
                return Failure(LegacyImportErrorCode.InvalidUtf8, exception.Message, sourceBytes, sha256);
            }
            catch (JsonException exception)
            {
                return Failure(LegacyImportErrorCode.InvalidJson, exception.Message, sourceBytes, sha256);
            }
            catch (LegacyValidationException exception)
            {
                return Failure(exception.Code, exception.Message, sourceBytes, sha256);
            }
            catch (InvalidOperationException exception)
            {
                return Failure(LegacyImportErrorCode.NonFiniteNumber, exception.Message, sourceBytes, sha256);
            }
        }

        public LegacyConversionResult Convert(byte[] sourceBytes, LegacyReferenceCatalog references)
        {
            var inspection = Inspect(sourceBytes, references);
            if (!inspection.IsValid)
            {
                return new LegacyConversionResult(inspection, null);
            }

            // Inspect already validated the same immutable byte sequence. Parse a
            // fresh tree so callers cannot observe or retain internal mutations.
            var root = Parse(sourceBytes);
            var seed = root.Value<uint>("seed");
            var simulationTime = root.Value<double>("time");
            var legacyPlayer = (JObject)root["player"];
            var legacyLocation = (JObject)legacyPlayer["location"];
            var player = (JObject)legacyPlayer.DeepClone();
            player.Remove("location");

            var envelope = new SaveEnvelopeV2
            {
                Seed = seed,
                SimulationTime = simulationTime,
                PlayerLocation = new PlayerLocationV2(
                    legacyLocation.Value<string>("systemId"),
                    legacyLocation.Value<string>("dockedAt"),
                    legacyLocation.Value<double>("x"),
                    legacyLocation.Value<double>("y")),
                Player = player,
                RngState = DeriveRngState(seed, simulationTime),
                NextEntityId = DeriveNextEntityId(root),
                LegacySourceSha256 = inspection.SourceSha256,
            };
            envelope.Validate();
            return new LegacyConversionResult(inspection, envelope);
        }

        private static JObject Parse(byte[] sourceBytes)
        {
            var text = StrictUtf8.GetString(sourceBytes);
            using (var stringReader = new StringReader(text))
            using (var reader = new JsonTextReader(stringReader))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                reader.MaxDepth = 64;
                var root = JObject.Load(reader, new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                });

                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment)
                    {
                        throw new JsonReaderException(Tr("Legacy save contains trailing JSON content."));
                    }
                }

                return root;
            }
        }

        private static void Validate(JObject root, LegacyReferenceCatalog references)
        {
            try
            {
                SaveEnvelopeV2.ValidateFiniteNumbers(root);
            }
            catch (InvalidOperationException exception)
            {
                throw new LegacyValidationException(LegacyImportErrorCode.NonFiniteNumber, exception.Message);
            }

            var version = RequiredInteger(root, "version");
            if (version != 1L)
            {
                throw new LegacyValidationException(LegacyImportErrorCode.WrongVersion,
                    Tr("Expected legacy version 1, got {0}.", version));
            }

            var seed = RequiredInteger(root, "seed");
            if (seed < uint.MinValue || seed > uint.MaxValue)
            {
                throw InvalidValue("seed", "must fit an unsigned 32-bit integer");
            }

            var simulationTime = RequiredNumber(root, "time");
            if (simulationTime < 0d)
            {
                throw InvalidValue("time", "must be non-negative");
            }

            var currentSystemId = RequiredString(root, "currentSystemId");
            RequireReference(references.HasSystem(currentSystemId), "currentSystemId", currentSystemId);

            var player = RequiredObject(root, "player");
            RequiredString(player, "name");
            var empire = RequiredString(player, "empire");
            RequireReference(references.HasFaction(empire), "player.empire", empire);
            RequiredNumber(player, "credits");

            var location = RequiredObject(player, "location");
            var locationSystemId = RequiredString(location, "systemId");
            RequireReference(references.HasSystem(locationSystemId), "player.location.systemId", locationSystemId);
            if (!string.Equals(currentSystemId, locationSystemId, StringComparison.Ordinal))
            {
                throw InvalidValue("currentSystemId", "must match player.location.systemId");
            }

            OptionalReference(location, "dockedAt", references.HasStation);
            RequiredNumber(location, "x");
            RequiredNumber(location, "y");
            OptionalReference(player, "homeSystemId", references.HasSystem);
            OptionalReference(player, "homeStationId", references.HasStation);
            OptionalReference(player, "destination", references.HasSystem);

            ValidateFactionMap(player, "lp", references);
            ValidateFactionMap(player, "standings", references);
            ValidateFactionMap(player, "missionCounts", references);
            ValidateInventory(player, "cargo", references.HasItem);
            ValidateInventory(player, "hangar", references.HasModule);
            ValidateShips(player, references);
            ValidateMissions(player, references);
        }

        private static void ValidateShips(JObject player, LegacyReferenceCatalog references)
        {
            var ships = RequiredArray(player, "ships");
            if (ships.Count == 0)
            {
                throw InvalidValue("player.ships", "must contain at least one ship");
            }

            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in ships)
            {
                if (!(token is JObject ship))
                {
                    throw InvalidValue("player.ships", "must contain only objects");
                }

                var instanceId = RequiredString(ship, "instId");
                if (!instanceIds.Add(instanceId))
                {
                    throw new LegacyValidationException(LegacyImportErrorCode.DuplicateId,
                        Tr("Duplicate ship instance ID '{0}'.", instanceId));
                }

                var shipId = RequiredString(ship, "shipId");
                RequireReference(references.HasShip(shipId), "player.ships.shipId", shipId);
                var fitting = RequiredObject(ship, "fitting");
                ValidateFittingArray(fitting, "high", references);
                ValidateFittingArray(fitting, "mid", references);
                ValidateFittingArray(fitting, "low", references);
            }

            var activeShip = RequiredString(player, "activeShip");
            if (!instanceIds.Contains(activeShip))
            {
                throw new LegacyValidationException(LegacyImportErrorCode.UnknownReference,
                    Tr("{0} references unknown instance '{1}'.", "player.activeShip", activeShip));
            }
        }

        private static void ValidateFittingArray(JObject fitting, string slot, LegacyReferenceCatalog references)
        {
            var modules = RequiredArray(fitting, slot);
            foreach (var module in modules)
            {
                if (module.Type == JTokenType.Null)
                {
                    continue;
                }

                if (module.Type != JTokenType.String)
                {
                    throw InvalidValue($"fitting.{slot}", "entries must be module IDs or null");
                }

                var moduleId = module.Value<string>();
                RequireReference(references.HasModule(moduleId), $"fitting.{slot}", moduleId);
            }
        }

        private static void ValidateMissions(JObject player, LegacyReferenceCatalog references)
        {
            if (!(player["missions"] is JArray missions))
            {
                throw Missing("player.missions");
            }

            var missionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in missions)
            {
                if (!(token is JObject mission))
                {
                    throw InvalidValue("player.missions", "must contain only objects");
                }

                var missionId = RequiredString(mission, "id");
                if (!missionIds.Add(missionId))
                {
                throw new LegacyValidationException(LegacyImportErrorCode.DuplicateId,
                    Tr("Duplicate mission ID '{0}'.", missionId));
                }

                OptionalReference(mission, "faction", references.HasFaction);
                OptionalReference(mission, "targetFaction", references.HasFaction);
                OptionalReference(mission, "pirateFaction", references.HasFaction);
                OptionalReference(mission, "targetSystemId", references.HasSystem);
                OptionalReference(mission, "destSystemId", references.HasSystem);
                OptionalReference(mission, "stationId", references.HasStation);
                OptionalReference(mission, "destStationId", references.HasStation);
                OptionalReference(mission, "agentId", references.HasAgent);
                OptionalReference(mission, "oreId", references.HasItem);
            }
        }

        private static void ValidateFactionMap(JObject player, string propertyName, LegacyReferenceCatalog references)
        {
            var map = RequiredObject(player, propertyName);
            foreach (var property in map.Properties())
            {
                RequireReference(references.HasFaction(property.Name), $"player.{propertyName}", property.Name);
                RequireNonNegativeNumber(property.Value, $"player.{propertyName}.{property.Name}", allowNegative: propertyName == "standings");
            }
        }

        private static void ValidateInventory(JObject player, string propertyName, Func<string, bool> referenceCheck)
        {
            var inventory = RequiredObject(player, propertyName);
            foreach (var property in inventory.Properties())
            {
                RequireReference(referenceCheck(property.Name), $"player.{propertyName}", property.Name);
                RequireNonNegativeNumber(property.Value, $"player.{propertyName}.{property.Name}", false);
            }
        }

        private static void OptionalReference(JObject owner, string propertyName, Func<string, bool> referenceCheck)
        {
            var token = owner[propertyName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return;
            }

            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
            {
                throw InvalidValue(propertyName, "must be a non-empty reference ID or null");
            }

            var id = token.Value<string>();
            RequireReference(referenceCheck(id), propertyName, id);
        }

        private static JObject RequiredObject(JObject owner, string propertyName)
        {
            if (!(owner[propertyName] is JObject value))
            {
                throw Missing(propertyName);
            }

            return value;
        }

        private static JArray RequiredArray(JObject owner, string propertyName)
        {
            if (!(owner[propertyName] is JArray value))
            {
                throw Missing(propertyName);
            }

            return value;
        }

        private static string RequiredString(JObject owner, string propertyName)
        {
            var token = owner[propertyName];
            if (token == null)
            {
                throw Missing(propertyName);
            }

            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace(token.Value<string>()))
            {
                throw InvalidValue(propertyName, "must be a non-empty string");
            }

            return token.Value<string>();
        }

        private static long RequiredInteger(JObject owner, string propertyName)
        {
            var token = owner[propertyName];
            if (token == null)
            {
                throw Missing(propertyName);
            }

            if (token.Type != JTokenType.Integer)
            {
                throw InvalidValue(propertyName, "must be an integer");
            }

            try
            {
                return token.Value<long>();
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException)
            {
                throw InvalidValue(propertyName, "is outside the supported integer range");
            }
        }

        private static double RequiredNumber(JObject owner, string propertyName)
        {
            var token = owner[propertyName];
            if (token == null)
            {
                throw Missing(propertyName);
            }

            return RequireNumber(token, propertyName);
        }

        private static void RequireNonNegativeNumber(JToken token, string path, bool allowNegative)
        {
            var number = RequireNumber(token, path);
            if (!allowNegative && number < 0d)
            {
                throw InvalidValue(path, "must be non-negative");
            }
        }

        private static double RequireNumber(JToken token, string path)
        {
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                throw InvalidValue(path, "must be numeric");
            }

            var number = System.Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture);
            if (!SaveEnvelopeV2.IsFinite(number))
            {
                throw new LegacyValidationException(LegacyImportErrorCode.NonFiniteNumber,
                    Tr("{0} must be finite.", path));
            }

            return number;
        }

        private static void RequireReference(bool exists, string path, string id)
        {
            if (!exists)
            {
                throw new LegacyValidationException(LegacyImportErrorCode.UnknownReference,
                    Tr("{0} references unknown ID '{1}'.", path, id));
            }
        }

        private static LegacyValidationException Missing(string path)
        {
            return new LegacyValidationException(LegacyImportErrorCode.MissingField, Tr("Required field '{0}' is missing.", path));
        }

        private static LegacyValidationException InvalidValue(string path, string reason)
        {
            return new LegacyValidationException(LegacyImportErrorCode.InvalidValue, Tr("Field '{0}' {1}.", path, Tr(reason)));
        }

        private static uint DeriveRngState(uint seed, double simulationTime)
        {
            var ticksModulo = (uint)(Math.Floor(simulationTime / SimulationStepSeconds) % UInt32Range);
            return unchecked(seed + ticksModulo * MulberryIncrement);
        }

        private static ulong DeriveNextEntityId(JToken root)
        {
            ulong maximum = 0;
            foreach (var value in SaveEnvelopeV2.EnumerateTokens(root))
            {
                if (value.Type != JTokenType.String)
                {
                    continue;
                }

                var text = value.Value<string>();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var parts = text.Split('_');
                foreach (var part in parts)
                {
                    if (ulong.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                    {
                        maximum = Math.Max(maximum, parsed);
                    }
                }
            }

            return maximum == ulong.MaxValue ? ulong.MaxValue : maximum + 1UL;
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static LegacyImportInspection Failure(LegacyImportErrorCode code, string message,
            byte[] sourceBytes, string sha256)
        {
            return new LegacyImportInspection(false, code, message, sourceBytes.Length, sha256,
                null, null, null, null, null);
        }

        private sealed class LegacyValidationException : Exception
        {
            public LegacyValidationException(LegacyImportErrorCode code, string message)
                : base(message)
            {
                Code = code;
            }

            public LegacyImportErrorCode Code { get; }
        }
    }
}
