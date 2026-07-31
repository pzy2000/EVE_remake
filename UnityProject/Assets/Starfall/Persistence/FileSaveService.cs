using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Starfall.Persistence
{
    public sealed class FileSaveService : ISaveService
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false, true);

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Culture = CultureInfo.InvariantCulture,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Error,
            TypeNameHandling = TypeNameHandling.None,
            MaxDepth = 128,
        };

        public FileSaveService()
            : this(Path.Combine(Application.persistentDataPath, "Saves"))
        {
        }

        public FileSaveService(string saveDirectory)
        {
            if (string.IsNullOrWhiteSpace(saveDirectory))
            {
                throw new ArgumentException("Save directory is required.", nameof(saveDirectory));
            }

            SaveDirectory = Path.GetFullPath(saveDirectory);
        }

        public string SaveDirectory { get; }

        public IReadOnlyList<SaveSlotInfo> List()
        {
            var result = new List<SaveSlotInfo>(SaveSlots.All.Count);
            foreach (var slot in SaveSlots.All)
            {
                result.Add(ReadSlotInfo(slot));
            }

            return result.AsReadOnly();
        }

        public void Save(SaveSlot slot, SaveEnvelopeV2 envelope)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            envelope.Validate();
            var json = JsonConvert.SerializeObject(envelope, JsonSettings);
            var path = GetSlotPath(slot);
            var backupPath = GetBackupPath(slot);
            var temporaryPath = GetTemporaryPath(slot);

            Directory.CreateDirectory(SaveDirectory);
            try
            {
                WriteDurable(temporaryPath, json);
                if (File.Exists(path))
                {
                    ReplaceWithBackup(temporaryPath, path, backupPath);
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void ReplaceWithBackup(string temporaryPath, string path, string backupPath)
        {
            try
            {
                File.Replace(temporaryPath, path, backupPath, true);
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException || exception is NotSupportedException)
            {
                // Some Android/Mono filesystem combinations do not expose File.Replace. Renames on the
                // app-private filesystem are atomic; moving primary to .bak first guarantees that a
                // force-stop always leaves at least one complete readable copy.
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(path, backupPath);
                try
                {
                    File.Move(temporaryPath, path);
                }
                catch
                {
                    if (!File.Exists(path) && File.Exists(backupPath)) File.Move(backupPath, path);
                    throw;
                }
            }
        }

        public SaveEnvelopeV2 Load(SaveSlot slot)
        {
            var path = GetSlotPath(slot);
            var backupPath = GetBackupPath(slot);
            Exception primaryError = null;

            if (File.Exists(path))
            {
                try
                {
                    return ReadAndValidate(path);
                }
                catch (Exception exception) when (IsSaveReadFailure(exception))
                {
                    primaryError = exception;
                }
            }

            if (File.Exists(backupPath))
            {
                try
                {
                    return ReadAndValidate(backupPath);
                }
                catch (Exception backupError) when (IsSaveReadFailure(backupError))
                {
                    throw new InvalidDataException($"Save slot '{slot.FileStem()}' and its backup are unreadable.",
                        new AggregateException(primaryError ?? new FileNotFoundException("Primary save is missing."), backupError));
                }
            }

            if (primaryError != null)
            {
                throw new InvalidDataException($"Save slot '{slot.FileStem()}' is unreadable and has no valid backup.", primaryError);
            }

            throw new FileNotFoundException($"Save slot '{slot.FileStem()}' does not exist.", path);
        }

        public string GetSlotPath(SaveSlot slot)
        {
            return Path.Combine(SaveDirectory, $"{slot.FileStem()}.json");
        }

        public string GetBackupPath(SaveSlot slot)
        {
            return GetSlotPath(slot) + ".bak";
        }

        public string GetTemporaryPath(SaveSlot slot)
        {
            return GetSlotPath(slot) + ".tmp";
        }

        private SaveSlotInfo ReadSlotInfo(SaveSlot slot)
        {
            var path = GetSlotPath(slot);
            var backupPath = GetBackupPath(slot);
            var anyFile = File.Exists(path) || File.Exists(backupPath);
            if (!anyFile)
            {
                return new SaveSlotInfo(slot, false, false, false, null, null, null, null, null);
            }

            try
            {
                var envelope = Load(slot);
                var recovered = File.Exists(backupPath) && (!File.Exists(path) || !CanRead(path));
                var sourcePath = recovered ? backupPath : path;
                return new SaveSlotInfo(slot, true, false, recovered,
                    envelope.Player.Value<string>("name"),
                    ReadNullableInt64(envelope.Player["credits"]),
                    envelope.PlayerLocation.SystemId,
                    envelope.SimulationTime,
                    ReadSavedAtUtc(envelope) ?? File.GetLastWriteTimeUtc(sourcePath));
            }
            catch (Exception exception) when (IsSaveReadFailure(exception))
            {
                return new SaveSlotInfo(slot, true, true, false, null, null, null, null, null);
            }
        }

        private static long? ReadNullableInt64(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return null;
            }

            try
            {
                return token.Value<long>();
            }
            catch (Exception exception) when (exception is FormatException || exception is OverflowException)
            {
                return null;
            }
        }

        private static DateTime? ReadSavedAtUtc(SaveEnvelopeV2 envelope)
        {
            if (string.IsNullOrEmpty(envelope?.SavedAtUtc)) return null;
            return DateTime.TryParse(envelope.SavedAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var savedAt)
                ? savedAt
                : (DateTime?)null;
        }

        private bool CanRead(string path)
        {
            try
            {
                ReadAndValidate(path);
                return true;
            }
            catch (Exception exception) when (IsSaveReadFailure(exception))
            {
                return false;
            }
        }

        private static SaveEnvelopeV2 ReadAndValidate(string path)
        {
            string json;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream, Utf8WithoutBom, true))
            {
                json = reader.ReadToEnd();
            }

            var envelope = JsonConvert.DeserializeObject<SaveEnvelopeV2>(json, JsonSettings);
            if (envelope == null)
            {
                throw new InvalidDataException("Save document is empty.");
            }

            envelope.Validate();
            return envelope;
        }

        private static void WriteDurable(string path, string json)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, Utf8WithoutBom))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(true);
            }
        }

        private static bool IsSaveReadFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is UnauthorizedAccessException ||
                   exception is JsonException ||
                   exception is InvalidOperationException;
        }
    }
}
