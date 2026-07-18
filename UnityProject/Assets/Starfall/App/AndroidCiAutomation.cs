#if STARFALL_ANDROID_CI
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Starfall.Presentation;
using Starfall.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starfall.App
{
    /// <summary>
    /// Strict smoke-build-only ADB automation surface. No arbitrary Execute
    /// string or caller-provided stable ID is accepted.
    /// </summary>
    public sealed partial class AppRoot
    {
        private const string AndroidCiCommandEvidence = "starfall-ci-command.json";
        private const string AndroidCiLowMemoryEvidence = "starfall-ci-low-memory.json";
        private int androidCiLowMemoryGeneration;

        public void OnAndroidCiCommand(string payload)
        {
            var requestId = 0;
            var command = string.Empty;
            try
            {
                var request = JObject.Parse(payload ?? string.Empty);
                requestId = request.Value<int?>("requestId") ?? 0;
                command = request.Value<string>("command") ?? string.Empty;
                if (requestId <= 0) throw new InvalidDataException("requestId must be positive");

                var detail = ExecuteAndroidCiCommand(command);
                WriteAndroidCiCommandEvidence(requestId, command, true, detail);
            }
            catch (Exception exception)
            {
                WriteAndroidCiCommandEvidence(
                    requestId,
                    command,
                    false,
                    exception.GetType().Name + ": " + exception.Message);
            }
        }

        private string ExecuteAndroidCiCommand(string command)
        {
            switch (command)
            {
                case "status":
                    return "status";
                case "start-new-game":
                    if (session != null) throw new InvalidOperationException("A session already exists");
                    StartNewGame("CI Pilot", "aurelian");
                    return "new game queued";
                case "undock":
                    RequireSession();
                    if (!session.State.Docked) throw new InvalidOperationException("Player is already in space");
                    Execute("undock");
                    return "undock queued";
                case "select-first-station":
                    RequireSpaceSession();
                    var station = session.State.Universe.Systems[session.State.Player.CurrentSystemId]
                        .Stations.Find(value => value != null);
                    if (station == null) throw new InvalidOperationException("Current system has no station");
                    Execute("select", station.Id);
                    return station.Id;
                case "select-first-gate":
                    RequireSpaceSession();
                    var gate = session.State.Universe.Systems[session.State.Player.CurrentSystemId]
                        .Gates.Find(value => value != null);
                    if (gate == null) throw new InvalidOperationException("Current system has no gate");
                    Execute("select", gate.Id);
                    return gate.Id;
                case "select-first-hostile":
                    RequireSpaceSession();
                    EntityState hostile = null;
                    foreach (var entity in session.State.Entities)
                    {
                        if (entity.Kind != EntityKind.Npc || entity.Dead || !IsHostile(entity)) continue;
                        hostile = entity;
                        break;
                    }
                    if (hostile == null) throw new InvalidOperationException("Current system has no hostile ship");
                    Execute("select", hostile.Id);
                    return hostile.Id;
                case "warp-selected":
                    RequireSelectedSpaceTarget();
                    Execute("warp", session.State.SelectedId);
                    return session.State.SelectedId;
                case "dock-or-jump":
                    RequireSelectedSpaceTarget();
                    Execute("dock", session.State.SelectedId);
                    return session.State.SelectedId;
                case "lock-selected":
                    RequireSelectedSpaceTarget();
                    Execute("lock", session.State.SelectedId);
                    return session.State.SelectedId;
                case "activate-modules":
                    RequireSpaceSession();
                    for (var index = 0; index < 9; index++) Execute("module", index.ToString());
                    return "modules toggled";
                case "save":
                    RequireSession();
                    Execute("save");
                    return "save queued";
                case "seed-low-memory-fixture":
                    RequireSpaceSession();
                    if (!spacePresenter)
                        throw new InvalidOperationException("Space presenter is unavailable");
                    var seededVfx = spacePresenter.SeedAndroidCiLowMemoryFixture();
                    var seededShipCache = ProceduralShipFactory.SeedAndroidCiDisposableCache();
                    if (seededVfx + seededShipCache < 2)
                        throw new InvalidOperationException("Low-memory fixture was not seeded");
                    return $"seeded vfx={seededVfx}, shipCache={seededShipCache}";
                case "show-death-overlay":
                    RequireSpaceSession();
                    session.ShowDeathOverlayForAndroidCi();
                    MarkUiDirty();
                    return "test fixture death overlay requested";
                default:
                    throw new InvalidDataException("Command is not in the Android CI whitelist");
            }
        }

        private void RequireSession()
        {
            if (session == null) throw new InvalidOperationException("No active session");
        }

        private void RequireSpaceSession()
        {
            RequireSession();
            if (session.State.Docked) throw new InvalidOperationException("Command requires Space");
        }

        private void RequireSelectedSpaceTarget()
        {
            RequireSpaceSession();
            if (string.IsNullOrEmpty(session.State.SelectedId))
                throw new InvalidOperationException("No selected stable target ID");
        }

        private void WriteAndroidCiCommandEvidence(
            int requestId,
            string command,
            bool success,
            string detail)
        {
            try
            {
                var state = session?.State;
                var playerEntity = state?.PlayerEntity();
                var evidence = new JObject
                {
                    ["requestId"] = requestId,
                    ["command"] = command ?? string.Empty,
                    ["status"] = success ? "ACK" : "ERR",
                    ["detail"] = detail ?? string.Empty,
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["hasSession"] = state != null,
                    ["docked"] = state?.Docked,
                    ["systemId"] = state?.Player?.CurrentSystemId,
                    ["selectedId"] = state?.SelectedId,
                    ["lockedTargetId"] = playerEntity?.LockedTargetId,
                    ["movement"] = playerEntity?.Movement.ToString(),
                    ["playerDead"] = state?.PlayerDead,
                    ["jumps"] = state?.Player?.Stats?.Jumps ?? 0,
                    ["graphicsDeviceType"] = SystemInfo.graphicsDeviceType.ToString(),
                    ["graphicsDeviceName"] = SystemInfo.graphicsDeviceName ?? string.Empty,
                    ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
                };
                if (playerEntity?.Modules != null && playerEntity.Modules.Count > 0)
                    evidence["module0Active"] = playerEntity.Modules[0].Active;
                if (spacePresenter)
                {
                    evidence["cameraYawDegrees"] = spacePresenter.AndroidCiCameraYawDegrees;
                    evidence["cameraDistance"] = spacePresenter.AndroidCiCameraDistance;
                    if (spacePresenter.TryGetAndroidCiTouchTarget(
                            out var touchTargetId, out var touchTargetPosition))
                    {
                        evidence["touchTargetId"] = touchTargetId;
                        evidence["touchTargetX"] = touchTargetPosition.x;
                        evidence["touchTargetY"] = touchTargetPosition.y;
                    }
                    if (spacePresenter.TryGetAndroidCiDragPath(out var dragStart, out var dragEnd))
                    {
                        evidence["dragStartX"] = dragStart.x;
                        evidence["dragStartY"] = dragStart.y;
                        evidence["dragEndX"] = dragEnd.x;
                        evidence["dragEndY"] = dragEnd.y;
                    }
                }
                var compact = evidence.ToString(Formatting.None);
                File.WriteAllText(
                    Path.Combine(Application.persistentDataPath, AndroidCiCommandEvidence),
                    evidence.ToString(Formatting.Indented));
                Debug.Log("STARFALL_ANDROID_CI_COMMAND_" + (success ? "ACK=" : "ERR=") + compact);
            }
            catch (Exception exception)
            {
                Debug.LogError("STARFALL_ANDROID_CI_COMMAND_ERR={\"detail\":\"evidence write failed: " +
                    exception.Message.Replace("\"", "'") + "\"}");
            }
        }

        private void WriteAndroidCiLowMemoryEvidence(
            int releasedTransientVfx,
            int releasedSpaceCacheEntries,
            int releasedShipCacheEntries)
        {
            try
            {
                androidCiLowMemoryGeneration++;
                var evidence = new JObject
                {
                    ["generation"] = androidCiLowMemoryGeneration,
                    ["scene"] = SceneManager.GetActiveScene().name,
                    ["releasedTransientVfx"] = releasedTransientVfx,
                    ["releasedSpaceCacheEntries"] = releasedSpaceCacheEntries,
                    ["releasedShipCacheEntries"] = releasedShipCacheEntries,
                    ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
                };
                File.WriteAllText(
                    Path.Combine(Application.persistentDataPath, AndroidCiLowMemoryEvidence),
                    evidence.ToString(Formatting.Indented));
                Debug.Log("STARFALL_ANDROID_CI_LOW_MEMORY=" + evidence.ToString(Formatting.None));
            }
            catch (Exception exception)
            {
                Debug.LogError("STARFALL_ANDROID_CI_LOW_MEMORY_ERR=" + exception.Message);
            }
        }
    }
}
#endif
