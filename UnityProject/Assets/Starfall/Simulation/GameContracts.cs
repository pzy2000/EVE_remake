using System;
using System.Collections;
using System.Collections.Generic;
using Starfall.Domain;

namespace Starfall.Simulation
{
    public enum GameCommandType
    {
        Select,
        Approach,
        Orbit,
        Warp,
        Lock,
        DockOrJump,
        Undock,
        ActivateModule,
        Buy,
        Sell,
        Fit,
        Unfit,
        SwitchShip,
        TalkToAgent,
        AcceptMission,
        CompleteMission,
        AbandonMission,
        SetDestination,
        Respawn,
        Repair,
        ExchangeLoyalty,
        Save,
    }

    public sealed class GameCommand
    {
        public GameCommand(GameCommandType type, string argument = null, int index = -1, SimVec2? position = null)
        {
            Type = type;
            Argument = argument;
            Index = index;
            Position = position;
        }

        public GameCommandType Type { get; }
        public string Argument { get; }
        public int Index { get; }
        public SimVec2? Position { get; }
    }

    public enum SimulationEventType
    {
        Spawn,
        Despawn,
        Damage,
        Weapon,
        Warp,
        Dock,
        Jump,
        Mission,
        Inventory,
        Death,
        Selection,
        SystemPopulated,
        SaveRequested,
        Log,
    }

    public sealed class SimulationEvent
    {
        public SimulationEvent(SimulationEventType type, string sourceId = null, string targetId = null,
            string message = null, double value = 0d, string detail = null, SimVec2? position = null)
        {
            Type = type;
            SourceId = sourceId;
            TargetId = targetId;
            Message = message;
            Value = value;
            Detail = detail;
            Position = position;
        }

        public SimulationEventType Type { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public string Message { get; }
        public double Value { get; }
        public string Detail { get; }
        public SimVec2? Position { get; }
    }

    public sealed class SimulationEventBatch : IReadOnlyList<SimulationEvent>
    {
        public static readonly SimulationEventBatch Empty = new SimulationEventBatch(Array.Empty<SimulationEvent>());
        private readonly IReadOnlyList<SimulationEvent> events;

        public SimulationEventBatch(IReadOnlyList<SimulationEvent> events)
        {
            this.events = events ?? Array.Empty<SimulationEvent>();
        }

        public int Count => events.Count;
        public SimulationEvent this[int index] => events[index];
        public IEnumerator<SimulationEvent> GetEnumerator() => events.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public interface IGameSession
    {
        GameState State { get; }
        void Enqueue(GameCommand command);
        SimulationEventBatch AdvanceFrame(double unscaledDeltaSeconds);
    }
}
