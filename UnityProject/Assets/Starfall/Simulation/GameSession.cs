using System;
using System.Collections.Generic;
using Starfall.Content;
using Starfall.Domain;

namespace Starfall.Simulation
{
    /// <summary>
    /// Deterministic, fixed-step gameplay session. The class deliberately has no
    /// dependency on GameObjects, Unity physics, wall-clock time or Unity random.
    /// </summary>
    public sealed partial class GameSession : IGameSession
    {
        public const double FixedStepSeconds = 0.05d;
        private const double TwoPi = Math.PI * 2d;
        private const double CelestialStandOffPadding = 40d;
        private readonly Queue<GameCommand> commands = new Queue<GameCommand>();
        private readonly List<SimulationEvent> frameEvents = new List<SimulationEvent>();
        private readonly IContentCatalog catalog;
        private Mulberry32 random;
        private double accumulator;
        private bool directorateSpawned;

        private static readonly Dictionary<string, string> EmpireStarterShips = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { FactionIds.Aurelian, ShipIds.Acolyte },
            { FactionIds.Kaldari, ShipIds.Shrike },
            { FactionIds.Meridian, ShipIds.Wasp },
            { FactionIds.Varkhald, ShipIds.Fang },
        };

        private static readonly Dictionary<string, string> FactionWeapons = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { FactionIds.Aurelian, ModuleIds.PulseLaser },
            { FactionIds.Kaldari, ModuleIds.MissileLauncher },
            { FactionIds.Meridian, ModuleIds.Blaster },
            { FactionIds.Varkhald, ModuleIds.Autocannon },
            { FactionIds.BloodReavers, ModuleIds.PulseLaser },
            { FactionIds.Nathari, ModuleIds.MissileLauncher },
            { FactionIds.CrimsonHand, ModuleIds.Blaster },
            { FactionIds.Ashfang, ModuleIds.Autocannon },
            { FactionIds.Sisters, ModuleIds.PulseLaser },
            { FactionIds.Directorate, ModuleIds.Railgun },
        };

        private static readonly Dictionary<string, string[]> PirateHulls = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { FactionIds.BloodReavers, new[] { ShipIds.Acolyte, ShipIds.Templar, ShipIds.Dawnbringer } },
            { FactionIds.Nathari, new[] { ShipIds.Shrike, ShipIds.Heron, ShipIds.Rook } },
            { FactionIds.CrimsonHand, new[] { ShipIds.Wasp, ShipIds.Anvil, ShipIds.Mantis } },
            { FactionIds.Ashfang, new[] { ShipIds.Fang, ShipIds.Maul, ShipIds.Broadsword } },
        };

        private static readonly Dictionary<string, string[]> NavyHulls = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { FactionIds.Aurelian, new[] { ShipIds.Acolyte, ShipIds.Templar, ShipIds.Dawnbringer } },
            { FactionIds.Kaldari, new[] { ShipIds.Shrike, ShipIds.Heron, ShipIds.Rook } },
            { FactionIds.Meridian, new[] { ShipIds.Wasp, ShipIds.Anvil, ShipIds.Mantis } },
            { FactionIds.Varkhald, new[] { ShipIds.Fang, ShipIds.Maul, ShipIds.Broadsword } },
            { FactionIds.Sisters, new[] { ShipIds.Pilgrim, ShipIds.Pilgrim, ShipIds.Pilgrim } },
        };

        public GameSession(GeneratedUniverse universe, IContentCatalog content, string pilotName, string empireId)
        {
            if (universe == null) throw new ArgumentNullException(nameof(universe));
            catalog = content ?? throw new ArgumentNullException(nameof(content));
            if (!EmpireStarterShips.ContainsKey(empireId)) throw new ArgumentException("Unknown starting empire.", nameof(empireId));

            random = new Mulberry32(universe.Seed ^ 0x5F3759DFu);
            var startSystemId = universe.StartSystems[empireId];
            var startSystem = universe.Systems[startSystemId];
            if (startSystem.Stations.Count == 0) throw new InvalidOperationException("Starting system has no station.");
            var station = startSystem.Stations[0];
            var player = new PlayerState
            {
                Name = string.IsNullOrWhiteSpace(pilotName) ? "Pilot" : pilotName.Trim(),
                EmpireId = empireId,
                CurrentSystemId = startSystemId,
                DockedAtStationId = station.Id,
                HomeSystemId = startSystemId,
                HomeStationId = station.Id,
                X = station.Position.X,
                Z = station.Position.Z,
            };
            player.Standings[empireId] = 1d;
            player.Hangar[ModuleIds.MiningLaser] = 1;
            var starter = CreateShipInstance(EmpireStarterShips[empireId], "ship_start");
            var weapon = FactionWeapons[empireId];
            starter.Fitting.High[0] = weapon;
            if (starter.Fitting.High.Count > 1) starter.Fitting.High[1] = weapon;
            if (starter.Fitting.Mid.Count > 0) starter.Fitting.Mid[0] = ModuleIds.ShieldBooster;
            player.Ships.Add(starter);
            player.ActiveShipInstanceId = starter.InstanceId;

            State = new GameState
            {
                Seed = universe.Seed,
                Universe = universe,
                Player = player,
                NextEntityId = 1,
                RngState = random.State,
            };
        }

        public GameSession(GeneratedUniverse universe, IContentCatalog content, PlayerState player,
            double simulationTime, uint rngState, ulong nextEntityId)
        {
            if (universe == null) throw new ArgumentNullException(nameof(universe));
            catalog = content ?? throw new ArgumentNullException(nameof(content));
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (!universe.Systems.ContainsKey(player.CurrentSystemId)) throw new ArgumentException("Player references an unknown system.", nameof(player));
            random = new Mulberry32(rngState);
            State = new GameState
            {
                Seed = universe.Seed,
                Universe = universe,
                Player = player,
                SimulationTime = Math.Max(0d, simulationTime),
                RngState = rngState,
                NextEntityId = Math.Max(1UL, nextEntityId),
            };
            if (!State.Docked)
            {
                SpawnPlayer(new SimVec2(player.X, player.Z));
                PopulateSystem();
            }
        }

        public GameState State { get; }

        public double CargoUsedVolume => CargoUsed();
        public double CargoCapacityVolume => CargoCapacity();
        public long PriceAtCurrentStation(string itemId) => State.Docked ? StationPrice(itemId) : 0L;

        public double RequiredAgentStanding(int level) => Math.Max(0d, (Math.Max(1, level) - 1) * 1.5d);

        public double PlayerStanding(string factionId)
        {
            State.Player.Standings.TryGetValue(factionId ?? string.Empty, out var standing);
            return standing;
        }

        public void Enqueue(GameCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            commands.Enqueue(command);
        }

        public SimulationEventBatch AdvanceFrame(double unscaledDeltaSeconds)
        {
            frameEvents.Clear();
            if (double.IsNaN(unscaledDeltaSeconds) || double.IsInfinity(unscaledDeltaSeconds) || unscaledDeltaSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(unscaledDeltaSeconds));
            accumulator += Math.Min(unscaledDeltaSeconds, 0.25d);
            while (accumulator + 1e-12d >= FixedStepSeconds)
            {
                Step(FixedStepSeconds);
                accumulator -= FixedStepSeconds;
            }
            State.RngState = random.State;
            return frameEvents.Count == 0
                ? SimulationEventBatch.Empty
                : new SimulationEventBatch(frameEvents.ToArray());
        }

        private void Step(double dt)
        {
            while (commands.Count > 0) Execute(commands.Dequeue());
            State.SimulationTime += dt;
            if (State.Player.CriminalTimer > 0d)
                State.Player.CriminalTimer = Math.Max(0d, State.Player.CriminalTimer - dt);
            if (State.Docked || State.PlayerDead) return;

            for (var i = 0; i < State.entities.Count; i++)
            {
                var entity = State.entities[i];
                if (entity.Dead) continue;
                UpdateCooldownsAndRegen(entity, dt);
                UpdateMovement(entity, dt);
            }
            UpdatePlayerModules();
            UpdateNpcAi(dt);
            CleanupDeadEntities();
            if (State.Player.CriminalTimer > 0d && !directorateSpawned) SpawnDirectorateResponse();

            var player = State.PlayerEntity();
            if (player != null)
            {
                State.Player.X = player.Position.X;
                State.Player.Z = player.Position.Z;
            }
        }

        private void Execute(GameCommand command)
        {
            switch (command.Type)
            {
                case GameCommandType.Select: Select(command.Argument); break;
                case GameCommandType.Approach: BeginMove(MovementMode.Approach, command.Argument, command.Position, 5d); break;
                case GameCommandType.Orbit: BeginMove(MovementMode.Orbit, command.Argument, command.Position, 50d); break;
                case GameCommandType.Warp: BeginWarp(command.Argument, command.Position); break;
                case GameCommandType.Lock: LockTarget(command.Argument); break;
                case GameCommandType.DockOrJump: DockOrJump(command.Argument); break;
                case GameCommandType.Undock: Undock(); break;
                case GameCommandType.ActivateModule: ActivateModule(command.Index); break;
                case GameCommandType.Buy: Buy(command.Argument); break;
                case GameCommandType.Sell: Sell(command.Argument); break;
                case GameCommandType.Fit: Fit(command.Argument); break;
                case GameCommandType.Unfit: Unfit(command.Argument); break;
                case GameCommandType.SwitchShip: SwitchShip(command.Argument); break;
                case GameCommandType.TalkToAgent: TalkToAgent(command.Argument); break;
                case GameCommandType.AcceptMission: AcceptMission(command.Argument); break;
                case GameCommandType.CompleteMission: CompleteMission(command.Argument); break;
                case GameCommandType.AbandonMission: AbandonMission(command.Argument); break;
                case GameCommandType.SetDestination: SetDestination(command.Argument); break;
                case GameCommandType.Respawn: Respawn(); break;
                case GameCommandType.Repair: Repair(); break;
                case GameCommandType.ExchangeLoyalty: ExchangeLoyalty(); break;
                case GameCommandType.Save:
                    SyncPlayerShip();
                    Emit(SimulationEventType.SaveRequested, message: "Manual save requested.", detail: "slot1");
                    break;
            }
        }

        private void Select(string id)
        {
            State.SelectedId = id ?? string.Empty;
            Emit(SimulationEventType.Selection, targetId: State.SelectedId);
        }

        private void BeginMove(MovementMode mode, string id, SimVec2? explicitPosition, double distance)
        {
            var player = State.PlayerEntity();
            if (player == null) return;
            var targetId = string.IsNullOrEmpty(id) ? State.SelectedId : id;
            var isCelestialTarget = TryResolveCelestial(targetId, out var celestialCenter,
                out var celestialRadius);
            var position = SimVec2.Zero;
            if (isCelestialTarget)
            {
                if (mode == MovementMode.Approach)
                {
                    if (!TryResolveCommandPosition(targetId, player.Position, out position,
                            out isCelestialTarget)) return;
                }
                else
                {
                    position = celestialCenter;
                    distance += Math.Max(0d, celestialRadius);
                }
            }
            else if (explicitPosition.HasValue) position = explicitPosition.Value;
            else if (!TryResolvePosition(targetId, out position)) return;
            player.Movement = mode;
            // Celestials are static. Keeping the ID here would make UpdateMovement
            // resolve their centre again and overwrite the safe stand-off point.
            player.MoveTargetId = isCelestialTarget && mode == MovementMode.Approach
                ? string.Empty
                : targetId ?? string.Empty;
            player.MoveTargetPosition = position;
            player.DesiredDistance = distance;
        }

        private void BeginWarp(string id, SimVec2? explicitPosition)
        {
            var player = State.PlayerEntity();
            if (player == null) return;
            var targetId = string.IsNullOrEmpty(id) ? State.SelectedId : id;
            var position = SimVec2.Zero;
            if (TryResolveCelestial(targetId, out _, out _))
            {
                if (!TryResolveCommandPosition(targetId, player.Position, out position, out _)) return;
            }
            else if (explicitPosition.HasValue) position = explicitPosition.Value;
            else if (!TryResolveCommandPosition(targetId, player.Position, out position, out _)) return;
            player.WarpTarget = position;
            player.WarpPhaseTime = 0d;
            player.Movement = MovementMode.WarpAlign;
            Emit(SimulationEventType.Warp, player.Id, targetId, "Warp drive active.", detail: "start");
            Log("Warp drive active.");
        }

        private void LockTarget(string id)
        {
            var player = State.PlayerEntity();
            if (player == null) return;
            var targetId = string.IsNullOrEmpty(id) ? State.SelectedId : id;
            var target = State.FindEntity(targetId);
            if (target == null || target == player)
            {
                Log("Only ships can be locked.");
                return;
            }
            if (SimVec2.Distance(player.Position, target.Position) > player.LockRange)
            {
                Log("Target is outside lock range.");
                return;
            }
            player.LockedTargetId = target.Id;
            Log($"Target locked: {target.Name}.");
        }

        private void DockOrJump(string id)
        {
            var player = State.PlayerEntity();
            if (player == null) return;
            var targetId = string.IsNullOrEmpty(id) ? State.SelectedId : id;
            var system = CurrentSystem();
            var station = system.Stations.Find(value => value.Id == targetId);
            if (station != null)
            {
                if (SimVec2.Distance(player.Position, station.Position) > 40d)
                {
                    Log("Move within 40 m before docking.");
                    return;
                }
                SyncPlayerShip();
                State.Player.DockedAtStationId = station.Id;
                State.Player.X = station.Position.X;
                State.Player.Z = station.Position.Z;
                CompleteDockObjectives(station.Id);
                State.entities.Clear();
                State.asteroids.Clear();
                Emit(SimulationEventType.Dock, "player", station.Id, $"Docked at {station.Name}.");
                Emit(SimulationEventType.SaveRequested, detail: "auto");
                return;
            }

            var gate = system.Gates.Find(value => value.Id == targetId);
            if (gate == null) return;
            if (SimVec2.Distance(player.Position, gate.Position) > 35d)
            {
                Log("Move within 35 m before jumping.");
                return;
            }
            Jump(gate);
        }

        private void Undock()
        {
            if (!State.Docked || State.PlayerDead) return;
            var station = CurrentSystem().Stations.Find(value => value.Id == State.Player.DockedAtStationId);
            if (station == null) return;
            State.Player.DockedAtStationId = string.Empty;
            var position = station.Position + new SimVec2(70d, 22d);
            SpawnPlayer(position);
            PopulateSystem();
            Emit(SimulationEventType.Dock, station.Id, "player", $"Undocked from {station.Name}.", detail: "undock");
            Emit(SimulationEventType.SaveRequested, detail: "auto");
        }

        private void Jump(GateDefinition gate)
        {
            SyncPlayerShip();
            var previousSystemId = State.Player.CurrentSystemId;
            State.Player.CurrentSystemId = gate.DestinationSystemId;
            State.Player.Stats.Jumps++;
            var destination = CurrentSystem();
            var arrivalGate = destination.Gates.Find(value => value.DestinationSystemId == previousSystemId);
            var position = arrivalGate != null ? arrivalGate.Position + new SimVec2(50d, 20d) : new SimVec2(300d, 0d);
            State.Player.X = position.X;
            State.Player.Z = position.Z;
            State.entities.Clear();
            SpawnPlayer(position);
            PopulateSystem();
            State.SelectedId = string.Empty;
            directorateSpawned = false;
            Emit(SimulationEventType.Jump, gate.Id, destination.Id, $"Jump complete: {destination.Name}.");
            Emit(SimulationEventType.SaveRequested, detail: "auto");
        }

        private void ActivateModule(int index)
        {
            var player = State.PlayerEntity();
            if (player == null || index < 0 || index >= player.Modules.Count) return;
            var runtime = player.Modules[index];
            var definition = catalog.Modules[runtime.ModuleId];
            switch (definition.Kind)
            {
                case ModuleKind.Weapon:
                case ModuleKind.Mining:
                    runtime.Active = !runtime.Active;
                    break;
                case ModuleKind.Propulsion:
                    player.AfterburnerOn = !player.AfterburnerOn;
                    RecomputeDerived(player);
                    runtime.Active = player.AfterburnerOn;
                    break;
                case ModuleKind.ShieldBoost:
                    if (runtime.Cooldown <= 0d && player.Shield < player.MaxShield)
                    {
                        player.Shield = Math.Min(player.MaxShield, player.Shield + definition.RepairAmount);
                        runtime.Cooldown = Math.Max(0.1d, definition.CycleTime);
                        Emit(SimulationEventType.Damage, player.Id, player.Id, "Shield restored.", -definition.RepairAmount, definition.Id);
                    }
                    break;
                case ModuleKind.ArmorRepair:
                    if (runtime.Cooldown <= 0d && player.Armor < player.MaxArmor)
                    {
                        player.Armor = Math.Min(player.MaxArmor, player.Armor + definition.RepairAmount);
                        runtime.Cooldown = Math.Max(0.1d, definition.CycleTime);
                        Emit(SimulationEventType.Damage, player.Id, player.Id, "Armor restored.", -definition.RepairAmount, definition.Id);
                    }
                    break;
            }
        }

        private void UpdatePlayerModules()
        {
            var player = State.PlayerEntity();
            if (player == null) return;
            for (var i = 0; i < player.Modules.Count; i++)
            {
                var runtime = player.Modules[i];
                if (!runtime.Active || runtime.Cooldown > 0d) continue;
                var definition = catalog.Modules[runtime.ModuleId];
                if (definition.Kind == ModuleKind.Mining)
                {
                    var asteroid = State.FindAsteroid(State.SelectedId);
                    if (asteroid != null && SimVec2.Distance(player.Position, asteroid.Position) <= definition.Range)
                        Mine(player, runtime, definition, asteroid);
                    continue;
                }
                if (definition.Kind != ModuleKind.Weapon) continue;
                var target = State.FindEntity(player.LockedTargetId);
                if (target != null) FireWeapon(player, runtime, definition, target);
            }
        }

        private void FireWeapon(EntityState shooter, RuntimeModuleState runtime, ModuleDefinition module, EntityState target)
        {
            if (runtime.Cooldown > 0d || target.Dead) return;
            if (SimVec2.Distance(shooter.Position, target.Position) > module.Range) return;
            if (shooter.Kind == EntityKind.Player && IsCriminalAttack(target.FactionId) && State.Player.CriminalTimer <= 0d)
                ApplyCrime(target.FactionId);
            runtime.Cooldown = Math.Max(0.1d, module.CycleTime);
            var damage = module.Damage * shooter.DamageMultiplier * (0.85d + random.NextDouble() * 0.3d);
            Emit(SimulationEventType.Weapon, shooter.Id, target.Id, module.Name, damage,
                module.Projectile ? "projectile:" + module.Id : "beam:" + module.Id);
            ApplyDamage(target, damage, shooter);
        }

        private void ApplyDamage(EntityState target, double amount, EntityState attacker)
        {
            if (target.Dead || amount <= 0d) return;
            var remaining = amount;
            target.LastDamageAt = State.SimulationTime;
            target.LastAttackerId = attacker != null ? attacker.Id : string.Empty;
            var shield = Math.Min(target.Shield, remaining);
            target.Shield -= shield;
            remaining -= shield;
            var armor = Math.Min(target.Armor, remaining);
            target.Armor -= armor;
            remaining -= armor;
            if (remaining > 0d) target.Hull -= remaining;
            Emit(SimulationEventType.Damage, attacker != null ? attacker.Id : null, target.Id, value: amount,
                detail: $"{target.Shield:R}|{target.Armor:R}|{target.Hull:R}");
            if (target.Hull <= 0d) Kill(target, attacker);
        }

        private void Kill(EntityState target, EntityState attacker)
        {
            if (target.Dead) return;
            target.Dead = true;
            Emit(SimulationEventType.Death, attacker != null ? attacker.Id : null, target.Id,
                $"{target.Name} destroyed.", position: target.Position);
            if (target.Kind == EntityKind.Player)
            {
                SyncPlayerShip();
                State.PlayerDead = true;
                State.Player.Stats.Deaths++;
                Log($"Your {catalog.Ships[target.ShipId].Name} was destroyed!");
                return;
            }
            if (attacker == null || attacker.Kind != EntityKind.Player) return;
            State.Player.Stats.Kills++;
            if (catalog.Factions[target.FactionId].Kind == FactionKind.Pirate)
            {
                var shipClass = catalog.Ships[target.ShipId].Class;
                var baseBounty = shipClass == ShipClass.Frigate ? 8000L : shipClass == ShipClass.Destroyer ? 25000L : shipClass == ShipClass.Cruiser ? 90000L : 350000L;
                var security = CurrentSystem().Security;
                var bounty = (long)JsMath.Round(baseBounty * (1d + Math.Max(0d, 0.5d - security)));
                State.Player.Credits += bounty;
                Log($"Bounty: +{bounty:N0} ISK for destroying {target.Name}.");
            }
            ModifyStanding(target.FactionId, catalog.Ships[target.ShipId].Class == ShipClass.Frigate ? -0.1d : -0.25d);
            if (!string.IsNullOrEmpty(target.MissionId)) OnMissionKill(target.MissionId);
        }

        private void Mine(EntityState player, RuntimeModuleState runtime, ModuleDefinition module, AsteroidState asteroid)
        {
            var quantity = Math.Min(module.MiningYield, asteroid.Amount);
            if (quantity <= 0d) return;
            var used = CargoUsed();
            var volume = catalog.Items[asteroid.OreId].Volume * quantity;
            if (used + volume > CargoCapacity() + 1e-9d)
            {
                runtime.Active = false;
                Log("Cargo hold full!");
                return;
            }
            runtime.Cooldown = Math.Max(0.1d, module.CycleTime);
            asteroid.Amount = Math.Max(0d, asteroid.Amount - quantity);
            AddQuantity(State.Player.Cargo, asteroid.OreId, quantity);
            State.Player.Stats.OreMined += quantity;
            Emit(SimulationEventType.Weapon, player.Id, asteroid.Id, module.Name, quantity, "mining:" + asteroid.OreId);
            Emit(SimulationEventType.Inventory, asteroid.Id, player.Id, $"+{quantity:0} {catalog.Items[asteroid.OreId].Name}", quantity, asteroid.OreId);
            if (asteroid.Amount <= 0d)
            {
                runtime.Active = false;
                ClearTargetReferences(asteroid.Id);
                State.asteroids.Remove(asteroid);
                Emit(SimulationEventType.Despawn, asteroid.Id, message: "Asteroid depleted.");
            }
        }

        private void UpdateCooldownsAndRegen(EntityState entity, double dt)
        {
            for (var i = 0; i < entity.Modules.Count; i++)
                entity.Modules[i].Cooldown = Math.Max(0d, entity.Modules[i].Cooldown - dt);
            if (State.SimulationTime - entity.LastDamageAt > 6d && entity.Shield < entity.MaxShield)
                entity.Shield = Math.Min(entity.MaxShield, entity.Shield + entity.MaxShield * 0.02d * dt);
        }

        private void UpdateMovement(EntityState entity, double dt)
        {
            if (entity.Movement == MovementMode.WarpAlign || entity.Movement == MovementMode.WarpCruise || entity.Movement == MovementMode.WarpDecelerate)
            {
                UpdateWarp(entity, dt);
                return;
            }
            if (!string.IsNullOrEmpty(entity.MoveTargetId) && TryResolvePosition(entity.MoveTargetId, out var resolved))
                entity.MoveTargetPosition = resolved;
            var to = entity.MoveTargetPosition - entity.Position;
            var distance = to.Magnitude;
            var desiredSpeed = 0d;
            if (entity.Movement == MovementMode.Approach || entity.Movement == MovementMode.Patrol)
            {
                if (distance > entity.DesiredDistance)
                {
                    entity.HeadingRadians = LerpAngle(entity.HeadingRadians, Math.Atan2(to.Z, to.X), dt * 3d);
                    desiredSpeed = entity.Movement == MovementMode.Patrol ? entity.MaxSpeed * 0.4d : entity.MaxSpeed;
                }
                else if (entity.Movement == MovementMode.Approach)
                {
                    entity.Movement = MovementMode.Idle;
                }
                else if (entity.Waypoints.Count > 0)
                {
                    entity.WaypointIndex = (entity.WaypointIndex + 1) % entity.Waypoints.Count;
                    entity.MoveTargetPosition = entity.Waypoints[entity.WaypointIndex];
                }
            }
            else if (entity.Movement == MovementMode.Orbit)
            {
                var baseAngle = Math.Atan2(to.Z, to.X);
                var desiredAngle = distance > entity.DesiredDistance * 1.15d ? baseAngle : baseAngle + Math.PI * 0.5d;
                entity.HeadingRadians = LerpAngle(entity.HeadingRadians, desiredAngle, dt * 3d);
                desiredSpeed = entity.MaxSpeed * 0.85d;
            }
            else if (entity.Movement == MovementMode.Flee)
            {
                entity.HeadingRadians = LerpAngle(entity.HeadingRadians, Math.Atan2(-to.Z, -to.X), dt * 3d);
                desiredSpeed = entity.MaxSpeed;
            }
            entity.Speed = desiredSpeed;
            entity.Position += new SimVec2(Math.Cos(entity.HeadingRadians), Math.Sin(entity.HeadingRadians)) * (desiredSpeed * dt);
        }

        private void UpdateWarp(EntityState entity, double dt)
        {
            var to = entity.WarpTarget - entity.Position;
            var distance = to.Magnitude;
            var angle = Math.Atan2(to.Z, to.X);
            entity.WarpPhaseTime += dt;
            if (entity.Movement == MovementMode.WarpAlign)
            {
                entity.HeadingRadians = LerpAngle(entity.HeadingRadians, angle, dt * 4d);
                entity.Speed = Math.Min(entity.Speed + entity.MaxSpeed * 2d * dt, entity.MaxSpeed * 0.75d);
                if (entity.WarpPhaseTime > 1.2d)
                {
                    entity.Movement = MovementMode.WarpCruise;
                    entity.WarpPhaseTime = 0d;
                }
            }
            else if (entity.Movement == MovementMode.WarpCruise)
            {
                entity.HeadingRadians = angle;
                entity.Speed = entity.WarpSpeed;
                if (distance < 350d) entity.Movement = MovementMode.WarpDecelerate;
            }
            else
            {
                entity.Speed = Math.Max(entity.MaxSpeed, entity.Speed - entity.WarpSpeed * 1.5d * dt);
                if (distance < 30d || entity.Speed <= entity.MaxSpeed + 1d)
                {
                    entity.Position = entity.WarpTarget;
                    entity.Speed = 0d;
                    entity.Movement = MovementMode.Idle;
                    Emit(SimulationEventType.Warp, entity.Id, message: "Warp drive deactivated.", detail: "end");
                    return;
                }
            }
            entity.Position += new SimVec2(Math.Cos(entity.HeadingRadians), Math.Sin(entity.HeadingRadians)) * (entity.Speed * dt);
        }

        private void UpdateNpcAi(double dt)
        {
            var player = State.PlayerEntity();
            if (player == null) return;
            for (var i = 0; i < State.entities.Count; i++)
            {
                var npc = State.entities[i];
                if (npc.Kind != EntityKind.Npc || npc.Dead) continue;
                npc.AiTime += dt;
                EntityState target = State.FindEntity(npc.LockedTargetId);
                if (target == null && ShouldAggro(npc, player))
                {
                    target = player;
                    npc.LockedTargetId = player.Id;
                    Log($"{npc.Name} has engaged you!");
                }
                if (target == null)
                {
                    if (npc.Waypoints.Count > 0)
                    {
                        npc.Movement = MovementMode.Patrol;
                        npc.MoveTargetPosition = npc.Waypoints[npc.WaypointIndex % npc.Waypoints.Count];
                        npc.DesiredDistance = 30d;
                    }
                    continue;
                }
                var weaponRange = MaxWeaponRange(npc);
                var distance = SimVec2.Distance(npc.Position, target.Position);
                npc.MoveTargetId = target.Id;
                npc.DesiredDistance = weaponRange * 0.58d;
                npc.Movement = distance > weaponRange * 0.85d ? MovementMode.Approach : MovementMode.Orbit;
                for (var m = 0; m < npc.Modules.Count; m++)
                {
                    var runtime = npc.Modules[m];
                    var definition = catalog.Modules[runtime.ModuleId];
                    if (definition.Kind == ModuleKind.Weapon && runtime.Cooldown <= 0d)
                        FireWeapon(npc, runtime, definition, target);
                    else if (definition.Kind == ModuleKind.ShieldBoost && runtime.Cooldown <= 0d && npc.Shield < npc.MaxShield * 0.4d)
                    {
                        npc.Shield = Math.Min(npc.MaxShield, npc.Shield + definition.RepairAmount);
                        runtime.Cooldown = definition.CycleTime;
                    }
                }
                if (npc.AiBehavior == "pirate" && npc.Hull < npc.MaxHull * 0.3d && string.IsNullOrEmpty(npc.MissionId))
                {
                    npc.Movement = MovementMode.Flee;
                    npc.AiTime += dt;
                    if (npc.AiTime > 4d)
                    {
                        npc.Dead = true;
                        Emit(SimulationEventType.Despawn, npc.Id, message: "Hostile warped away.");
                    }
                }
            }
        }

        private bool ShouldAggro(EntityState npc, EntityState player)
        {
            if (SimVec2.Distance(npc.Position, player.Position) > npc.AggroRange) return false;
            return EntityDispositionPolicy.Evaluate(npc, State.Player, catalog, player.Id) ==
                   EntityDisposition.Hostile;
        }

        private void PopulateSystem()
        {
            var player = State.PlayerEntity();
            State.entities.RemoveAll(entity => entity.Kind == EntityKind.Npc);
            State.asteroids.Clear();
            var system = CurrentSystem();
            var visit = State.VisitCounter++;

            for (var beltIndex = 0; beltIndex < system.Belts.Count; beltIndex++)
            {
                var belt = system.Belts[beltIndex];
                var beltRandom = new Mulberry32(Fnv1a.HashString(system.Id + belt.Id));
                for (var i = 0; i < belt.AsteroidCount; i++)
                {
                    var angle = beltRandom.Range(0d, TwoPi);
                    var radius = beltRandom.Range(30d, 220d);
                    State.asteroids.Add(new AsteroidState
                    {
                        Id = belt.Id + "_a" + i,
                        BeltId = belt.Id,
                        OreId = belt.OreId,
                        Position = belt.Position + new SimVec2(Math.Cos(angle), Math.Sin(angle)) * radius,
                        Radius = beltRandom.Range(6d, 16d),
                        Amount = beltRandom.RangeInclusive(60, 220),
                    });
                }
            }

            var populationRandom = new Mulberry32(Fnv1a.HashString(system.Id + ":" + visit));
            var points = new List<SimVec2>();
            for (var i = 0; i < system.Gates.Count; i++) points.Add(system.Gates[i].Position);
            for (var i = 0; i < system.Stations.Count; i++) points.Add(system.Stations[i].Position);
            for (var i = 0; i < system.Belts.Count; i++) points.Add(system.Belts[i].Position);
            if (points.Count == 0) points.Add(SimVec2.Zero);

            if (system.Region == SystemRegion.Empire)
            {
                SpawnPatrol(system.FactionId, NavyHulls[system.FactionId], populationRandom.RangeInclusive(2, 3), "navy", points, populationRandom);
                var faction = catalog.Factions[system.FactionId];
                if (system.Belts.Count > 0 && populationRandom.Chance(0.3d) && !string.IsNullOrEmpty(faction.HomePirateId))
                    SpawnPatrol(faction.HomePirateId, PirateHulls[faction.HomePirateId], 1, "pirate", points, populationRandom);
            }
            else if (system.Region == SystemRegion.LowSecurity)
            {
                var owner = NavyHulls.ContainsKey(system.FactionId) ? system.FactionId : FactionIds.Aurelian;
                SpawnPatrol(owner, NavyHulls[owner], 2, "navy", points, populationRandom);
                var pirate = catalog.Factions.ContainsKey(system.FactionId) ? catalog.Factions[system.FactionId].HomePirateId : null;
                if (string.IsNullOrEmpty(pirate)) pirate = FactionIds.BloodReavers;
                SpawnPatrol(pirate, PirateHulls[pirate], populationRandom.RangeInclusive(2, 4), "pirate", points, populationRandom);
            }
            else if (system.Region == SystemRegion.NullSecurity)
            {
                var pirate = PirateHulls.ContainsKey(system.FactionId) ? system.FactionId : FactionIds.BloodReavers;
                SpawnPatrol(pirate, PirateHulls[pirate], populationRandom.RangeInclusive(3, 6), "pirate", points, populationRandom);
            }
            else
            {
                SpawnPatrol(FactionIds.Sisters, NavyHulls[FactionIds.Sisters], 2, "sisters", points, populationRandom);
            }
            SpawnMissionTargets(populationRandom);
            Emit(SimulationEventType.SystemPopulated, system.Id, message: $"{system.Name}: {State.entities.Count - (player == null ? 0 : 1)} traffic contacts.");
        }

        private void SpawnPatrol(string factionId, string[] hulls, int count, string behavior,
            IReadOnlyList<SimVec2> points, Mulberry32 source)
        {
            var anchor = points[source.RangeInclusive(0, points.Count - 1)];
            for (var i = 0; i < count; i++)
            {
                var hullIndex = Math.Min(source.RangeInclusive(0, count > 2 ? 2 : 1), hulls.Length - 1);
                var entity = CreateEntity(hulls[hullIndex], factionId, EntityKind.Npc,
                    anchor + new SimVec2(source.Range(-80d, 80d), source.Range(-80d, 80d)), behavior, null, false);
                for (var waypoint = 0; waypoint < Math.Min(3, points.Count); waypoint++)
                    entity.Waypoints.Add(points[(waypoint + i) % points.Count]);
                Emit(SimulationEventType.Spawn, entity.Id, message: entity.Name, detail: entity.ShipId);
            }
        }

        private void SpawnMissionTargets(Mulberry32 source)
        {
            for (var missionIndex = 0; missionIndex < State.Player.Missions.Count; missionIndex++)
            {
                var mission = State.Player.Missions[missionIndex];
                if (mission.Status != MissionStatus.Active || mission.TargetSystemId != State.Player.CurrentSystemId) continue;
                if (mission.Type != MissionType.Security && mission.Type != MissionType.StorylineKill) continue;
                var remaining = Math.Max(0, mission.KillsRequired - mission.Kills);
                var faction = string.IsNullOrEmpty(mission.TargetFactionId) ? FactionIds.BloodReavers : mission.TargetFactionId;
                var hulls = PirateHulls.ContainsKey(faction) ? PirateHulls[faction] : NavyHulls.ContainsKey(faction) ? NavyHulls[faction] : PirateHulls[FactionIds.BloodReavers];
                var missionRandom = new Mulberry32(Fnv1a.HashString(mission.Id));
                var angle = missionRandom.Range(0d, TwoPi);
                var anchor = new SimVec2(Math.Cos(angle), Math.Sin(angle)) * missionRandom.Range(1200d, 2400d);
                for (var i = 0; i < remaining; i++)
                {
                    var elite = mission.Type == MissionType.StorylineKill && i == 0;
                    var hull = hulls[Math.Min(Math.Max(0, mission.Level - 1 + (elite ? 1 : 0)), hulls.Length - 1)];
                    var npc = CreateEntity(hull, faction, EntityKind.Npc,
                        anchor + new SimVec2(missionRandom.Range(-120d, 120d), missionRandom.Range(-120d, 120d)),
                        "pirate", mission.Id, elite);
                    npc.AggroRange = 600d;
                    Emit(SimulationEventType.Spawn, npc.Id, message: npc.Name, detail: npc.ShipId);
                }
            }
        }

        private EntityState SpawnPlayer(SimVec2 position)
        {
            State.entities.RemoveAll(entity => entity.Kind == EntityKind.Player);
            var ship = State.Player.ActiveShip();
            if (ship == null) throw new InvalidOperationException("Player has no active ship.");
            var entity = CreateEntity(ship.ShipId, State.Player.EmpireId, EntityKind.Player, position, string.Empty, null, false, ship);
            entity.Id = "player";
            entity.Name = State.Player.Name + " (" + ship.Name + ")";
            return entity;
        }

        private EntityState CreateEntity(string shipId, string factionId, EntityKind kind, SimVec2 position,
            string behavior, string missionId, bool elite, ShipInstanceState instance = null)
        {
            var definition = catalog.Ships[shipId];
            var entity = new EntityState
            {
                Id = kind == EntityKind.Player ? "player" : NextId("ship"),
                Kind = kind,
                ShipId = shipId,
                FactionId = factionId,
                Name = catalog.Factions[factionId].Abbreviation + " " + definition.Name,
                Position = position,
                Shield = instance != null ? instance.Shield : definition.HitPoints.Shield,
                Armor = instance != null ? instance.Armor : definition.HitPoints.Armor,
                Hull = instance != null ? instance.Hull : definition.HitPoints.Hull,
                AiBehavior = behavior ?? string.Empty,
                MissionId = missionId ?? string.Empty,
                Elite = elite,
                AggroRange = string.IsNullOrEmpty(missionId) ? 350d : 600d,
            };
            if (instance != null)
            {
                AddModules(entity, instance.Fitting);
            }
            else
            {
                var fitting = CreateEmptyFitting(definition);
                var weapon = FactionWeapons.ContainsKey(factionId) ? FactionWeapons[factionId] : ModuleIds.PulseLaser;
                for (var i = 0; i < fitting.High.Count; i++) fitting.High[i] = weapon;
                if (definition.Class != ShipClass.Frigate && fitting.Mid.Count > 0) fitting.Mid[0] = ModuleIds.ShieldBooster;
                if (definition.Class != ShipClass.Frigate && fitting.Low.Count > 0) fitting.Low[0] = ModuleIds.DamageAmp;
                AddModules(entity, fitting);
                for (var i = 0; i < entity.Modules.Count; i++)
                    if (catalog.Modules[entity.Modules[i].ModuleId].Kind == ModuleKind.Weapon) entity.Modules[i].Active = true;
            }
            RecomputeDerived(entity);
            if (elite)
            {
                entity.MaxShield *= 1.8d;
                entity.MaxArmor *= 1.8d;
                entity.MaxHull *= 1.8d;
                entity.Shield = entity.MaxShield;
                entity.Armor = entity.MaxArmor;
                entity.Hull = entity.MaxHull;
                entity.DamageMultiplier *= 1.4d;
                entity.Name = "Elite " + entity.Name;
            }
            State.entities.Add(entity);
            return entity;
        }

        private void AddModules(EntityState entity, FittingState fitting)
        {
            foreach (var moduleId in fitting.All())
                entity.Modules.Add(new RuntimeModuleState { ModuleId = moduleId });
        }

        private void RecomputeDerived(EntityState entity)
        {
            var definition = catalog.Ships[entity.ShipId];
            entity.MaxShield = definition.HitPoints.Shield;
            entity.MaxArmor = definition.HitPoints.Armor;
            entity.MaxHull = definition.HitPoints.Hull;
            entity.MaxSpeed = definition.Speed;
            entity.WarpSpeed = definition.WarpSpeed;
            entity.LockRange = definition.LockRange;
            entity.DamageMultiplier = 1d;
            for (var i = 0; i < entity.Modules.Count; i++)
            {
                var module = catalog.Modules[entity.Modules[i].ModuleId];
                entity.MaxShield += module.ShieldBonus;
                entity.MaxArmor += module.ArmorBonus;
                if (module.DamageMultiplier > 0d) entity.DamageMultiplier *= module.DamageMultiplier;
            }
            if (entity.AfterburnerOn) entity.MaxSpeed *= catalog.Modules[ModuleIds.Afterburner].SpeedMultiplier;
            entity.Shield = Math.Min(entity.Shield, entity.MaxShield);
            entity.Armor = Math.Min(entity.Armor, entity.MaxArmor);
            entity.Hull = Math.Min(entity.Hull, entity.MaxHull);
        }

        private void Buy(string itemId)
        {
            if (!State.Docked || string.IsNullOrEmpty(itemId)) return;
            var price = StationPrice(itemId);
            if (price <= 0 || State.Player.Credits < price)
            {
                Log("Insufficient credits or item unavailable.");
                return;
            }
            if (catalog.Ships.ContainsKey(itemId))
            {
                var definition = catalog.Ships[itemId];
                if (definition.NpcOnly) return;
                State.Player.Credits -= price;
                var ship = CreateShipInstance(itemId, NextId("shipinst"));
                State.Player.Ships.Add(ship);
                Emit(SimulationEventType.Inventory, message: $"Purchased {definition.Name} for {price:N0} ISK.", detail: itemId);
            }
            else if (catalog.Modules.ContainsKey(itemId))
            {
                State.Player.Credits -= price;
                AddQuantity(State.Player.Hangar, itemId, 1);
                Emit(SimulationEventType.Inventory, message: $"Purchased {catalog.Modules[itemId].Name}.", detail: itemId);
            }
        }

        private void Sell(string itemId)
        {
            if (!State.Docked || string.IsNullOrEmpty(itemId)) return;
            if (catalog.Items.ContainsKey(itemId) && State.Player.Cargo.TryGetValue(itemId, out var quantity) && quantity > 0d)
            {
                var price = Math.Max(1L, (long)JsMath.Round(StationPrice(itemId) * 0.85d));
                State.Player.Credits += (long)JsMath.Round(price * quantity);
                State.Player.Cargo.Remove(itemId);
                Emit(SimulationEventType.Inventory, message: $"Sold {quantity:0}x {catalog.Items[itemId].Name}.", detail: itemId);
            }
            else if (catalog.Modules.ContainsKey(itemId) && State.Player.Hangar.TryGetValue(itemId, out var count) && count > 0)
            {
                State.Player.Hangar[itemId] = count - 1;
                if (count == 1) State.Player.Hangar.Remove(itemId);
                State.Player.Credits += Math.Max(1L, (long)JsMath.Round(StationPrice(itemId) * 0.85d));
                Emit(SimulationEventType.Inventory, message: $"Sold {catalog.Modules[itemId].Name}.", detail: itemId);
            }
        }

        private void Fit(string argument)
        {
            if (!State.Docked || string.IsNullOrEmpty(argument)) return;
            var parts = argument.Split('|');
            if (parts.Length != 4 || !int.TryParse(parts[2], out var index)) return;
            var ship = State.Player.Ships.Find(value => value.InstanceId == parts[0]);
            var moduleId = parts[3];
            if (ship == null || !catalog.Modules.TryGetValue(moduleId, out var module) || !State.Player.Hangar.TryGetValue(moduleId, out var count) || count <= 0) return;
            var slots = SlotList(ship.Fitting, parts[1]);
            if (slots == null || index < 0 || index >= slots.Count || !string.IsNullOrEmpty(slots[index])) return;
            if (!SlotMatches(module.Slot, parts[1])) return;
            slots[index] = moduleId;
            State.Player.Hangar[moduleId] = count - 1;
            if (count == 1) State.Player.Hangar.Remove(moduleId);
            Emit(SimulationEventType.Inventory, message: $"Fitted {module.Name}.", detail: moduleId);
        }

        private void Unfit(string argument)
        {
            if (!State.Docked || string.IsNullOrEmpty(argument)) return;
            var parts = argument.Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[2], out var index)) return;
            var ship = State.Player.Ships.Find(value => value.InstanceId == parts[0]);
            var slots = ship == null ? null : SlotList(ship.Fitting, parts[1]);
            if (slots == null || index < 0 || index >= slots.Count || string.IsNullOrEmpty(slots[index])) return;
            var moduleId = slots[index];
            slots[index] = null;
            AddQuantity(State.Player.Hangar, moduleId, 1);
            Emit(SimulationEventType.Inventory, message: $"Unfitted {catalog.Modules[moduleId].Name}.", detail: moduleId);
        }

        private void SwitchShip(string instanceId)
        {
            if (!State.Docked) return;
            var ship = State.Player.Ships.Find(value => value.InstanceId == instanceId);
            if (ship == null) return;
            State.Player.ActiveShipInstanceId = ship.InstanceId;
            Emit(SimulationEventType.Inventory, message: $"Active ship: {ship.Name}.", detail: ship.ShipId);
        }

        private void TalkToAgent(string agentId)
        {
            if (!State.Docked) return;
            AgentDefinition agent = null;
            for (var i = 0; i < CurrentSystem().Stations.Count && agent == null; i++)
                agent = CurrentSystem().Stations[i].Agents.Find(value => value.Id == agentId);
            if (agent == null) return;
            var station = CurrentSystem().Stations.Find(value => value.Id == agent.StationId);
            var factionId = station != null ? station.FactionId : CurrentSystem().FactionId;
            var requiredStanding = RequiredAgentStanding(agent.Level);
            var playerStanding = PlayerStanding(factionId);
            if (playerStanding + 1e-9d < requiredStanding)
            {
                Log($"{agent.Name} requires {requiredStanding:0.0} standing. Current standing: {playerStanding:0.0}.");
                return;
            }
            var existing = State.Player.Missions.Find(value => value.AgentId == agent.Id && value.Status != MissionStatus.Done);
            if (existing != null)
            {
                if (existing.Status == MissionStatus.Offered) AcceptMission(existing.Id);
                else Log(existing.Title + " — " + existing.ProgressText());
                return;
            }
            var mission = GenerateMission(agent);
            State.Player.Missions.Add(mission);
            Emit(SimulationEventType.Mission, agent.Id, mission.Id, "Mission offered: " + mission.Title, detail: "offered");
            AcceptMission(mission.Id);
        }

        private MissionState GenerateMission(AgentDefinition agent)
        {
            var system = CurrentSystem();
            var station = system.Stations.Find(value => value.Id == agent.StationId);
            var mission = new MissionState
            {
                Id = NextId("mis"),
                Status = MissionStatus.Offered,
                FactionId = station != null ? station.FactionId : system.FactionId,
                AgentId = agent.Id,
                AgentName = agent.Name,
                StationId = agent.StationId,
                Level = Math.Max(1, agent.Level),
                RewardStanding = 0.12d * Math.Max(1, agent.Level),
            };
            var division = (agent.Division ?? string.Empty).ToLowerInvariant();
            if (division.Contains("distribution"))
            {
                var destination = PickDestinationSystem(system.Id, 1 + mission.Level);
                var destinationStation = destination.Stations.Count > 0 ? destination.Stations[0] : station;
                mission.Type = MissionType.Distribution;
                mission.Title = "Courier: Sealed Dispatch";
                mission.Description = "Deliver encrypted cargo without opening the container.";
                mission.DestinationSystemId = destination.Id;
                mission.DestinationStationId = destinationStation.Id;
                mission.Quantity = 8d + mission.Level * 4d;
                mission.RewardCredits = 45000L * mission.Level;
                mission.RewardLoyaltyPoints = 35 * mission.Level;
            }
            else if (division.Contains("mining"))
            {
                mission.Type = MissionType.Mining;
                mission.Title = "Industrial: Ore Requisition";
                mission.Description = "Mine ore and return it to the agent's station.";
                mission.TargetSystemId = system.Id;
                mission.OreId = system.Security >= 0.5d ? ItemIds.Ferrite : system.Security >= 0d ? ItemIds.Novacite : ItemIds.Crystalline;
                mission.Quantity = 20d + mission.Level * 10d;
                mission.RewardCredits = 50000L * mission.Level;
                mission.RewardLoyaltyPoints = 30 * mission.Level;
            }
            else
            {
                mission.Type = MissionType.Security;
                mission.Title = "Security: Clear the Deadspace";
                mission.Description = "Destroy the hostile squad threatening local traffic.";
                mission.TargetSystemId = PickDestinationSystem(system.Id, Math.Max(1, mission.Level)).Id;
                var faction = catalog.Factions[mission.FactionId];
                mission.TargetFactionId = string.IsNullOrEmpty(faction.HomePirateId) ? FactionIds.BloodReavers : faction.HomePirateId;
                mission.KillsRequired = 2 + mission.Level;
                mission.RewardCredits = 60000L * mission.Level;
                mission.RewardLoyaltyPoints = 50 * mission.Level;
            }
            return mission;
        }

        private void AcceptMission(string missionId)
        {
            var mission = State.Player.Missions.Find(value => value.Id == missionId);
            if (mission == null || mission.Status != MissionStatus.Offered) return;
            if ((mission.Type == MissionType.Distribution || mission.Type == MissionType.StorylineHaul) && !AddCargo(ItemIds.SealedCargo, mission.Quantity))
            {
                Log($"Need {mission.Quantity:0} m3 free cargo space.");
                return;
            }
            mission.Status = MissionStatus.Active;
            var objectiveSystemId = !string.IsNullOrEmpty(mission.DestinationSystemId)
                ? mission.DestinationSystemId
                : mission.TargetSystemId;
            if (!string.IsNullOrEmpty(objectiveSystemId) && State.Universe.Systems.ContainsKey(objectiveSystemId))
                State.Player.DestinationSystemId = objectiveSystemId;
            Emit(SimulationEventType.Mission, mission.AgentId, mission.Id, "Mission accepted: " + mission.Title, detail: "active");
            if (!State.Docked) PopulateSystem();
        }

        private void CompleteMission(string missionId)
        {
            var mission = State.Player.Missions.Find(value => value.Id == missionId);
            if (mission == null || mission.Status != MissionStatus.ObjectivesMet) return;
            mission.Status = MissionStatus.Done;
            State.Player.Credits += mission.RewardCredits;
            AddQuantity(State.Player.LoyaltyPoints, mission.FactionId, mission.RewardLoyaltyPoints);
            ModifyStanding(mission.FactionId, mission.RewardStanding);
            State.Player.Stats.MissionsDone++;
            Emit(SimulationEventType.Mission, mission.AgentId, mission.Id,
                $"Mission complete: {mission.Title}. +{mission.RewardCredits:N0} ISK, +{mission.RewardLoyaltyPoints} LP.", detail: "done");
            if (mission.Type != MissionType.StorylineKill && mission.Type != MissionType.StorylineHaul)
            {
                AddQuantity(State.Player.MissionCounts, mission.FactionId, 1);
                if (State.Player.MissionCounts[mission.FactionId] % 5 == 0) OfferStoryline(mission.FactionId);
            }
        }

        private void AbandonMission(string missionId)
        {
            var mission = State.Player.Missions.Find(value => value.Id == missionId);
            if (mission == null || mission.Status == MissionStatus.Done) return;
            if (mission.Type == MissionType.Distribution || mission.Type == MissionType.StorylineHaul)
                RemoveCargo(ItemIds.SealedCargo, mission.Quantity);
            mission.Status = MissionStatus.Done;
            ModifyStanding(mission.FactionId, -0.2d);
            Emit(SimulationEventType.Mission, targetId: mission.Id, message: "Mission abandoned: " + mission.Title, detail: "done");
        }

        private void CompleteDockObjectives(string stationId)
        {
            for (var i = 0; i < State.Player.Missions.Count; i++)
            {
                var mission = State.Player.Missions[i];
                if (mission.Status != MissionStatus.Active) continue;
                if ((mission.Type == MissionType.Distribution || mission.Type == MissionType.StorylineHaul) && mission.DestinationStationId == stationId)
                {
                    if (RemoveCargo(ItemIds.SealedCargo, mission.Quantity))
                    {
                        mission.Status = MissionStatus.ObjectivesMet;
                        CompleteMission(mission.Id);
                    }
                }
                else if (mission.Type == MissionType.Mining && mission.StationId == stationId && RemoveCargo(mission.OreId, mission.Quantity))
                {
                    mission.Status = MissionStatus.ObjectivesMet;
                    CompleteMission(mission.Id);
                }
            }
        }

        private void OnMissionKill(string missionId)
        {
            var mission = State.Player.Missions.Find(value => value.Id == missionId && value.Status == MissionStatus.Active);
            if (mission == null) return;
            mission.Kills++;
            Emit(SimulationEventType.Mission, targetId: mission.Id, message: mission.ProgressText(), detail: "progress");
            if (mission.Kills >= mission.KillsRequired)
            {
                mission.Status = MissionStatus.ObjectivesMet;
                Emit(SimulationEventType.Mission, targetId: mission.Id, message: "Objectives complete: " + mission.Title, detail: "objectives_met");
            }
        }

        private void OfferStoryline(string factionId)
        {
            var kill = random.Chance(0.5d);
            var mission = new MissionState
            {
                Id = NextId("mis"),
                Type = kill ? MissionType.StorylineKill : MissionType.StorylineHaul,
                Status = MissionStatus.Offered,
                Title = kill ? "Storyline: Breaking the Blockade" : "Storyline: The Ambassador",
                Description = kill ? "Eliminate an elite blockade squad." : "Deliver a dignitary under absolute secrecy.",
                FactionId = factionId,
                AgentName = catalog.Factions[factionId].Name + " Command",
                Level = 2,
                RewardCredits = kill ? 350000L : 300000L,
                RewardLoyaltyPoints = kill ? 500 : 400,
                RewardStanding = 0.75d,
            };
            if (kill)
            {
                var candidates = new List<StarSystemDefinition>();
                foreach (var system in State.Universe.OrderedSystems) if (system.Security < 0.5d) candidates.Add(system);
                var target = candidates.Count > 0 ? candidates[random.RangeInclusive(0, candidates.Count - 1)] : State.Universe.OrderedSystems[0];
                mission.TargetSystemId = target.Id;
                mission.KillsRequired = 3;
                mission.TargetFactionId = catalog.Factions[factionId].HomePirateId ?? FactionIds.BloodReavers;
            }
            else
            {
                StarSystemDefinition home = null;
                foreach (var system in State.Universe.OrderedSystems)
                    if (system.FactionId == factionId && system.Stations.Count > 0) { home = system; break; }
                if (home == null) foreach (var system in State.Universe.OrderedSystems) if (system.Stations.Count > 0) { home = system; break; }
                mission.DestinationSystemId = home.Id;
                mission.DestinationStationId = home.Stations[0].Id;
                mission.Quantity = 40d;
                mission.TargetFactionId = catalog.Factions[factionId].HomePirateId ?? FactionIds.BloodReavers;
            }
            State.Player.Missions.Add(mission);
            Emit(SimulationEventType.Mission, targetId: mission.Id, message: "STORYLINE MISSION available: " + mission.Title, detail: "offered");
        }

        private void SetDestination(string systemId)
        {
            if (!State.Universe.Systems.ContainsKey(systemId)) return;
            State.Player.DestinationSystemId = systemId;
            var route = UniverseRoutes.FindRoute(State.Universe, State.Player.CurrentSystemId, systemId);
            Log(route == null ? "No route available." : $"Route set: {route.Count - 1} jumps to {State.Universe.Systems[systemId].Name}.");
        }

        private void Repair()
        {
            if (!State.Docked) return;
            var ship = State.Player.ActiveShip();
            if (ship == null) return;
            var definition = catalog.Ships[ship.ShipId];
            ship.Shield = definition.HitPoints.Shield;
            ship.Armor = definition.HitPoints.Armor;
            ship.Hull = definition.HitPoints.Hull;
            Emit(SimulationEventType.Inventory, message: ship.Name + " fully repaired.", detail: "repair");
        }

        private void ExchangeLoyalty()
        {
            const int cost = 100;
            if (!State.Docked) return;
            var factionId = State.Player.EmpireId;
            State.Player.LoyaltyPoints.TryGetValue(factionId, out var available);
            if (available < cost)
            {
                Log("Insufficient loyalty points. The module cache requires 100 LP.");
                return;
            }

            State.Player.LoyaltyPoints[factionId] = available - cost;
            AddQuantity(State.Player.Hangar, ModuleIds.DamageAmp, 1);
            Emit(SimulationEventType.Inventory,
                message: $"Exchanged 100 LP for {catalog.Modules[ModuleIds.DamageAmp].Name}.",
                detail: ModuleIds.DamageAmp);
        }

        private void Respawn()
        {
            if (!State.PlayerDead) return;
            var lost = State.Player.ActiveShip();
            var protectedByStarterInsurance = lost != null && State.Player.Stats.InsuranceClaims == 0;
            if (protectedByStarterInsurance)
            {
                var definition = catalog.Ships[lost.ShipId];
                var shieldBonus = 0d;
                var armorBonus = 0d;
                foreach (var moduleId in lost.Fitting.All())
                {
                    if (!catalog.Modules.TryGetValue(moduleId, out var fitted)) continue;
                    shieldBonus += fitted.ShieldBonus;
                    armorBonus += fitted.ArmorBonus;
                }
                lost.Shield = definition.HitPoints.Shield + shieldBonus;
                lost.Armor = definition.HitPoints.Armor + armorBonus;
                lost.Hull = definition.HitPoints.Hull;
                State.Player.Stats.InsuranceClaims++;
                Log("Starter insurance restored your ship and fitted modules. Future losses are permanent.");
            }
            else if (lost != null) State.Player.Ships.Remove(lost);
            if (State.Player.Ships.Count == 0)
            {
                var rookie = CreateShipInstance(EmpireStarterShips[State.Player.EmpireId], NextId("shipinst"));
                rookie.Fitting.High[0] = FactionWeapons[State.Player.EmpireId];
                if (rookie.Fitting.High.Count > 1) rookie.Fitting.High[1] = FactionWeapons[State.Player.EmpireId];
                if (rookie.Fitting.Mid.Count > 0) rookie.Fitting.Mid[0] = ModuleIds.ShieldBooster;
                State.Player.Ships.Add(rookie);
                Log("The Directorate issued you a rookie frigate.");
            }
            State.Player.ActiveShipInstanceId = State.Player.Ships[0].InstanceId;
            State.Player.CriminalTimer = 0d;
            State.Player.CurrentSystemId = State.Player.HomeSystemId;
            State.Player.DockedAtStationId = State.Player.HomeStationId;
            var station = CurrentSystem().Stations.Find(value => value.Id == State.Player.HomeStationId);
            State.Player.X = station.Position.X;
            State.Player.Z = station.Position.Z;
            State.PlayerDead = false;
            State.entities.Clear();
            State.asteroids.Clear();
            Emit(SimulationEventType.Dock, "player", station.Id, "Clone activated at home station.", detail: "respawn");
            Emit(SimulationEventType.SaveRequested, detail: "auto");
        }

        private void SpawnDirectorateResponse()
        {
            var player = State.PlayerEntity();
            if (player == null) return;
            directorateSpawned = true;
            for (var i = 0; i < 2; i++)
            {
                var response = CreateEntity(ShipIds.Enforcer, FactionIds.Directorate, EntityKind.Npc,
                    player.Position + new SimVec2(random.Range(-200d, 200d), random.Range(-200d, 200d)),
                    "police", null, false);
                response.LockedTargetId = player.Id;
                response.AggroRange = 99999d;
                Emit(SimulationEventType.Spawn, response.Id, message: response.Name, detail: response.ShipId);
            }
            Log("Directorate response units have warped in!");
        }

        private bool IsCriminalAttack(string targetFactionId)
        {
            if (!catalog.Factions.TryGetValue(targetFactionId, out var faction)) return false;
            if (faction.Kind == FactionKind.Pirate) return false;
            if (faction.Kind == FactionKind.Police || faction.Kind == FactionKind.Sisters) return true;
            return faction.Kind == FactionKind.Empire && CurrentSystem().Security >= 0.5d;
        }

        private void ApplyCrime(string targetFactionId)
        {
            State.Player.CriminalTimer = Math.Max(State.Player.CriminalTimer, 120d);
            ModifyStanding(targetFactionId, -0.5d);
            ModifyStanding(FactionIds.Directorate, -0.2d);
            Log("CRIMINAL ACT! The Directorate has been alerted.");
        }

        private void ModifyStanding(string factionId, double amount)
        {
            State.Player.Standings.TryGetValue(factionId, out var current);
            State.Player.Standings[factionId] = Clamp(current + amount, -10d, 10d);
        }

        private long StationPrice(string itemId)
        {
            long basePrice;
            if (catalog.Modules.TryGetValue(itemId, out var module)) basePrice = module.Price;
            else if (catalog.Ships.TryGetValue(itemId, out var ship)) basePrice = ship.Price;
            else if (catalog.Items.TryGetValue(itemId, out var item)) basePrice = item.BasePrice;
            else return 0;
            var stationId = State.Player.DockedAtStationId;
            var priceRandom = new Mulberry32(Fnv1a.HashString(stationId + ":" + itemId));
            var factor = 0.85d + priceRandom.NextDouble() * 0.45d;
            factor *= 1d + (0.5d - Math.Min(CurrentSystem().Security, 0.5d)) * 0.5d;
            return Math.Max(1L, (long)JsMath.Round(basePrice * factor));
        }

        private double CargoUsed()
        {
            var used = 0d;
            foreach (var pair in State.Player.Cargo)
                used += (catalog.Items.TryGetValue(pair.Key, out var item) ? item.Volume : 1d) * pair.Value;
            return used;
        }

        private double CargoCapacity()
        {
            var ship = State.Player.ActiveShip();
            if (ship == null) return 0d;
            var capacity = catalog.Ships[ship.ShipId].CargoCapacity;
            for (var i = 0; i < ship.Fitting.Low.Count; i++)
                if (ship.Fitting.Low[i] == ModuleIds.CargoExpander) capacity += catalog.Modules[ModuleIds.CargoExpander].CargoBonus;
            return capacity;
        }

        private bool AddCargo(string itemId, double quantity)
        {
            var volume = (catalog.Items.TryGetValue(itemId, out var item) ? item.Volume : 1d) * quantity;
            if (CargoUsed() + volume > CargoCapacity() + 1e-9d) return false;
            AddQuantity(State.Player.Cargo, itemId, quantity);
            return true;
        }

        private bool RemoveCargo(string itemId, double quantity)
        {
            if (!State.Player.Cargo.TryGetValue(itemId, out var current) || current + 1e-9d < quantity) return false;
            current -= quantity;
            if (current <= 1e-9d) State.Player.Cargo.Remove(itemId);
            else State.Player.Cargo[itemId] = current;
            return true;
        }

        private void SyncPlayerShip()
        {
            var entity = State.PlayerEntity();
            var ship = State.Player.ActiveShip();
            if (entity == null || ship == null) return;
            ship.Shield = Math.Max(0d, entity.Shield);
            ship.Armor = Math.Max(0d, entity.Armor);
            ship.Hull = Math.Max(0d, entity.Hull);
            State.Player.X = entity.Position.X;
            State.Player.Z = entity.Position.Z;
        }

        private ShipInstanceState CreateShipInstance(string shipId, string instanceId)
        {
            var definition = catalog.Ships[shipId];
            return new ShipInstanceState
            {
                InstanceId = instanceId,
                ShipId = shipId,
                Name = definition.Name,
                Fitting = CreateEmptyFitting(definition),
                Shield = definition.HitPoints.Shield,
                Armor = definition.HitPoints.Armor,
                Hull = definition.HitPoints.Hull,
            };
        }

        private static FittingState CreateEmptyFitting(ShipDefinition definition)
        {
            var fitting = new FittingState();
            for (var i = 0; i < definition.Slots.High; i++) fitting.High.Add(null);
            for (var i = 0; i < definition.Slots.Mid; i++) fitting.Mid.Add(null);
            for (var i = 0; i < definition.Slots.Low; i++) fitting.Low.Add(null);
            return fitting;
        }

        private bool TryResolvePosition(string id, out SimVec2 position)
        {
            var entity = State.FindEntity(id);
            if (entity != null) { position = entity.Position; return true; }
            var asteroid = State.FindAsteroid(id);
            if (asteroid != null) { position = asteroid.Position; return true; }
            var system = CurrentSystem();
            var station = system.Stations.Find(value => value.Id == id);
            if (station != null) { position = station.Position; return true; }
            var gate = system.Gates.Find(value => value.Id == id);
            if (gate != null) { position = gate.Position; return true; }
            var belt = system.Belts.Find(value => value.Id == id);
            if (belt != null) { position = belt.Position; return true; }
            if (TryResolveCelestial(id, out position, out _)) return true;
            position = SimVec2.Zero;
            return false;
        }

        private bool TryResolveCommandPosition(string id, SimVec2 actorPosition, out SimVec2 position,
            out bool isCelestialTarget)
        {
            if (!TryResolvePosition(id, out position))
            {
                isCelestialTarget = false;
                return false;
            }

            if (!TryResolveCelestial(id, out var center, out var radius))
            {
                isCelestialTarget = false;
                return true;
            }

            isCelestialTarget = true;
            var fromCenter = actorPosition - center;
            var magnitude = fromCenter.Magnitude;
            var direction = magnitude > 1e-9d
                ? fromCenter * (1d / magnitude)
                : new SimVec2(1d, 0d);
            position = center + direction * (Math.Max(0d, radius) + CelestialStandOffPadding);
            return true;
        }

        private bool TryResolveCelestial(string id, out SimVec2 position, out double radius)
        {
            var system = CurrentSystem();
            if (string.Equals(id, system.Id + "_star", StringComparison.Ordinal))
            {
                position = SimVec2.Zero;
                radius = system.Star.Radius;
                return true;
            }

            var planet = system.Planets.Find(value => value.Id == id);
            if (planet != null)
            {
                position = planet.Position;
                radius = planet.Radius;
                return true;
            }

            for (var i = 0; i < system.Planets.Count; i++)
            {
                var moon = system.Planets[i].Moons.Find(value => value.Id == id);
                if (moon == null) continue;
                position = moon.Position;
                radius = moon.Radius;
                return true;
            }

            position = SimVec2.Zero;
            radius = 0d;
            return false;
        }

        private StarSystemDefinition PickDestinationSystem(string fromSystemId, int maximumJumps)
        {
            var candidates = new List<string> { fromSystemId };
            var seen = new HashSet<string>(StringComparer.Ordinal) { fromSystemId };
            var frontier = new List<string> { fromSystemId };
            for (var depth = 0; depth < maximumJumps; depth++)
            {
                var next = new List<string>();
                for (var i = 0; i < frontier.Count; i++)
                {
                    foreach (var neighbor in State.Universe.Adjacency[frontier[i]])
                    {
                        if (!seen.Add(neighbor)) continue;
                        next.Add(neighbor);
                        if (State.Universe.Systems[neighbor].Stations.Count > 0) candidates.Add(neighbor);
                    }
                }
                frontier = next;
            }
            var id = candidates[random.RangeInclusive(0, candidates.Count - 1)];
            return State.Universe.Systems[id];
        }

        private StarSystemDefinition CurrentSystem() => State.Universe.Systems[State.Player.CurrentSystemId];

        private string NextId(string prefix)
        {
            var value = State.NextEntityId++;
            return prefix + "_" + value;
        }

        private void CleanupDeadEntities()
        {
            for (var i = State.entities.Count - 1; i >= 0; i--)
            {
                var entity = State.entities[i];
                if (!entity.Dead || entity.Kind == EntityKind.Player) continue;
                ClearTargetReferences(entity.Id);
                State.entities.RemoveAt(i);
                Emit(SimulationEventType.Despawn, entity.Id);
            }
        }

        private void ClearTargetReferences(string removedId)
        {
            if (string.IsNullOrEmpty(removedId)) return;
            if (string.Equals(State.SelectedId, removedId, StringComparison.Ordinal))
            {
                State.SelectedId = string.Empty;
                Emit(SimulationEventType.Selection, targetId: string.Empty);
            }

            for (var i = 0; i < State.entities.Count; i++)
            {
                var entity = State.entities[i];
                if (string.Equals(entity.LockedTargetId, removedId, StringComparison.Ordinal))
                {
                    entity.LockedTargetId = string.Empty;
                    if (entity.Kind == EntityKind.Player) DeactivateAllModules(entity);
                }
                if (string.Equals(entity.MoveTargetId, removedId, StringComparison.Ordinal))
                {
                    entity.MoveTargetId = string.Empty;
                    entity.Movement = MovementMode.Idle;
                }
            }
        }

        private void DeactivateAllModules(EntityState entity)
        {
            var recomputeSpeed = entity.AfterburnerOn;
            entity.AfterburnerOn = false;
            for (var i = 0; i < entity.Modules.Count; i++) entity.Modules[i].Active = false;
            if (recomputeSpeed) RecomputeDerived(entity);
            Log("Target lost. Active modules deactivated.");
        }

        private double MaxWeaponRange(EntityState entity)
        {
            var range = 60d;
            for (var i = 0; i < entity.Modules.Count; i++)
            {
                var module = catalog.Modules[entity.Modules[i].ModuleId];
                if (module.Kind == ModuleKind.Weapon) range = Math.Max(range, module.Range);
            }
            return range;
        }

        private void Emit(SimulationEventType type, string sourceId = null, string targetId = null,
            string message = null, double value = 0d, string detail = null, SimVec2? position = null)
        {
            frameEvents.Add(new SimulationEvent(type, sourceId, targetId, message, value, detail, position));
        }

        private void Log(string message)
        {
            Emit(SimulationEventType.Log, message: message);
        }

        private static List<string> SlotList(FittingState fitting, string slot)
        {
            if (slot == "high") return fitting.High;
            if (slot == "mid") return fitting.Mid;
            if (slot == "low") return fitting.Low;
            return null;
        }

        private static bool SlotMatches(SlotType type, string slot)
        {
            return (type == SlotType.High && slot == "high") ||
                   (type == SlotType.Mid && slot == "mid") ||
                   (type == SlotType.Low && slot == "low");
        }

        private static void AddQuantity(Dictionary<string, double> dictionary, string id, double quantity)
        {
            dictionary.TryGetValue(id, out var current);
            dictionary[id] = current + quantity;
        }

        private static void AddQuantity(Dictionary<string, int> dictionary, string id, int quantity)
        {
            dictionary.TryGetValue(id, out var current);
            dictionary[id] = current + quantity;
        }

        private static double LerpAngle(double current, double target, double amount)
        {
            var delta = (target - current + Math.PI) % TwoPi - Math.PI;
            if (delta < -Math.PI) delta += TwoPi;
            return current + delta * Clamp(amount, 0d, 1d);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }
    }
}
