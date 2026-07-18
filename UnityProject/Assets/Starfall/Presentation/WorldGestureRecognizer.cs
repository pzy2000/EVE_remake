using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.Presentation
{
    public enum WorldGestureType
    {
        Tap,
        DoubleTap,
        Drag,
        Pinch,
        LongPress,
    }

    public readonly struct WorldGestureEvent
    {
        public WorldGestureEvent(WorldGestureType type, Vector2 position, Vector2 delta, float scale)
        {
            Type = type;
            Position = position;
            Delta = delta;
            Scale = scale;
        }

        public WorldGestureType Type { get; }
        public Vector2 Position { get; }
        public Vector2 Delta { get; }
        public float Scale { get; }
    }

    public interface IWorldPointerBlocker
    {
        bool BlocksWorldPointer(Vector2 screenPosition);
    }

    public static class WorldPointerBlocker
    {
        public static IWorldPointerBlocker Current { get; set; }

        public static bool Blocks(Vector2 screenPosition)
        {
            return Current != null && Current.BlocksWorldPointer(screenPosition);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Current = null;
        }
    }

    /// <summary>
    /// A deterministic, DPI-aware recognizer shared by runtime touch input and EditMode tests.
    /// UI blocking is latched at pointer-down so a gesture that starts over UI can never leak
    /// into the 3D world after the pointer moves away from that control.
    /// </summary>
    public sealed class WorldGestureRecognizer
    {
        public const float DefaultFallbackDpi = 420f;
        public const float DragThresholdDp = 12f;
        public const float DoubleTapDistanceDp = 24f;
        public const double DoubleTapSeconds = 0.3d;
        public const double LongPressSeconds = 0.5d;

        private sealed class PointerState
        {
            public int Id;
            public Vector2 Start;
            public Vector2 Position;
            public Vector2 Previous;
            public double StartedAt;
            public bool Blocked;
            public bool AllowDrag;
            public bool Dragging;
            public bool LongPressFired;
            public bool SuppressTap;
        }

        private readonly Dictionary<int, PointerState> pointers = new();
        private float pixelsPerDp = DefaultFallbackDpi / 160f;
        private float previousPinchDistance;
        private bool pinchActive;
        private bool hasLastTap;
        private Vector2 lastTapPosition;
        private double lastTapTime;

        public int ActivePointerCount => pointers.Count;
        public float PixelsPerDp => pixelsPerDp;

        public void ConfigureDpi(float densityDpi)
        {
            var safeDpi = densityDpi > 0f && !float.IsNaN(densityDpi) && !float.IsInfinity(densityDpi)
                ? densityDpi
                : DefaultFallbackDpi;
            pixelsPerDp = Mathf.Max(1f, safeDpi / 160f);
        }

        public void Begin(int pointerId, Vector2 position, double time, bool blockedByUi, bool allowDrag = true)
        {
            if (blockedByUi) hasLastTap = false;
            var state = new PointerState
            {
                Id = pointerId,
                Start = position,
                Position = position,
                Previous = position,
                StartedAt = time,
                Blocked = blockedByUi,
                AllowDrag = allowDrag,
            };
            pointers[pointerId] = state;

            if (pointers.Count < 2) return;
            foreach (var pointer in pointers.Values) pointer.SuppressTap = true;
            if (TryGetPinchPair(out var first, out var second))
                previousPinchDistance = Vector2.Distance(first.Position, second.Position);
        }

        public WorldGestureEvent? Move(int pointerId, Vector2 position, double time)
        {
            if (!pointers.TryGetValue(pointerId, out var state)) return null;
            var frameDelta = position - state.Position;
            state.Previous = state.Position;
            state.Position = position;

            if (pointers.Count >= 2)
            {
                foreach (var pointer in pointers.Values)
                {
                    pointer.SuppressTap = true;
                    if (pointer.Blocked) return null;
                }
                if (!TryGetPinchPair(out var first, out var second)) return null;
                var currentDistance = Vector2.Distance(first.Position, second.Position);
                if (previousPinchDistance <= Mathf.Epsilon)
                {
                    previousPinchDistance = currentDistance;
                    return null;
                }
                var ratio = currentDistance / previousPinchDistance;
                previousPinchDistance = currentDistance;
                pinchActive = true;
                if (float.IsNaN(ratio) || float.IsInfinity(ratio) || Mathf.Abs(ratio - 1f) < 0.0001f) return null;
                return new WorldGestureEvent(WorldGestureType.Pinch,
                    (first.Position + second.Position) * 0.5f, Vector2.zero, ratio);
            }

            if (state.Blocked || state.LongPressFired) return null;
            var dragThreshold = DragThresholdDp * pixelsPerDp;
            if (!state.Dragging && Vector2.Distance(state.Start, position) >= dragThreshold)
            {
                state.SuppressTap = true;
                if (!state.AllowDrag) return null;
                state.Dragging = true;
            }
            return state.Dragging && frameDelta.sqrMagnitude > 0f
                ? new WorldGestureEvent(WorldGestureType.Drag, position, frameDelta, 1f)
                : null;
        }

        public WorldGestureEvent? Tick(double time)
        {
            if (pointers.Count != 1) return null;
            foreach (var state in pointers.Values)
            {
                if (state.Blocked || state.Dragging || state.LongPressFired || state.SuppressTap) return null;
                if (time - state.StartedAt < LongPressSeconds) return null;
                if (Vector2.Distance(state.Start, state.Position) > DragThresholdDp * pixelsPerDp) return null;
                state.LongPressFired = true;
                state.SuppressTap = true;
                return new WorldGestureEvent(WorldGestureType.LongPress, state.Position, Vector2.zero, 1f);
            }
            return null;
        }

        public WorldGestureEvent? End(int pointerId, Vector2 position, double time)
        {
            if (!pointers.TryGetValue(pointerId, out var state)) return null;
            state.Position = position;
            pointers.Remove(pointerId);

            if (pinchActive || pointers.Count > 0)
            {
                pinchActive = false;
                previousPinchDistance = 0f;
                foreach (var remaining in pointers.Values)
                {
                    remaining.Start = remaining.Position;
                    remaining.Previous = remaining.Position;
                    remaining.StartedAt = time;
                    remaining.SuppressTap = true;
                }
                return null;
            }

            if (state.Blocked)
            {
                hasLastTap = false;
                return null;
            }
            if (state.Dragging || state.LongPressFired || state.SuppressTap) return null;
            if (Vector2.Distance(state.Start, position) > DragThresholdDp * pixelsPerDp) return null;

            var isDoubleTap = hasLastTap && time - lastTapTime <= DoubleTapSeconds &&
                              Vector2.Distance(position, lastTapPosition) <= DoubleTapDistanceDp * pixelsPerDp;
            lastTapTime = time;
            lastTapPosition = position;
            hasLastTap = !isDoubleTap;
            return new WorldGestureEvent(isDoubleTap ? WorldGestureType.DoubleTap : WorldGestureType.Tap,
                position, Vector2.zero, 1f);
        }

        public void Cancel(int pointerId)
        {
            pointers.Remove(pointerId);
            pinchActive = false;
            previousPinchDistance = 0f;
            hasLastTap = false;
            foreach (var pointer in pointers.Values) pointer.SuppressTap = true;
        }

        public void Reset()
        {
            pointers.Clear();
            pinchActive = false;
            previousPinchDistance = 0f;
            hasLastTap = false;
        }

        private bool TryGetPinchPair(out PointerState first, out PointerState second)
        {
            first = null;
            second = null;
            foreach (var pointer in pointers.Values)
            {
                if (first == null) first = pointer;
                else
                {
                    second = pointer;
                    break;
                }
            }
            return first != null && second != null;
        }
    }
}
