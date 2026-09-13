using System.Collections.Generic;
using Starfall.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    /// <summary>
    /// Translates pointer gestures on the full-screen HUD backdrop into world
    /// actions. The HUD root acts as the touch surface: panels and overlays sit
    /// on top of it and keep their own events, so gestures only reach the world
    /// where no UI element is under the finger. One code path serves touch and
    /// mouse alike, which is what makes the space view playable on Android
    /// where no hardware mouse exists.
    /// </summary>
    public sealed class WorldBackdropInput
    {
        private const float TapMaxDurationSeconds = 0.45f;
        private const float TapSlopPixels = 24f;
        private const float DoubleTapMaxSeconds = 0.32f;
        private const float DoubleTapSlopPixels = 32f;
        private const long LongPressMilliseconds = 560;
        private const long LongPressPollMilliseconds = 50;

        private readonly VisualElement backdrop;
        private readonly Dictionary<int, PointerTrack> pointers = new Dictionary<int, PointerTrack>();

        // One recurring checker for the whole lifetime of this input: allocating
        // a fresh one-shot schedule item per PointerDown leaked a paused item on
        // every tap/drag, and UI Toolkit keeps paused items in the scheduler.
        private IVisualElementScheduledItem longPressItem;
        private bool longPressFired;
        private bool pinchEngaged;
        private int pinchPointerA = -1;
        private int pinchPointerB = -1;
        private bool disposed;
        private float lastTapTime = -10f;
        private Vector2 lastTapPosition;

        private sealed class PointerTrack
        {
            public Vector2 DownPosition;
            public Vector2 LastPosition;
            public float DownTime;
            public int Button;
            public bool Moved;
        }

        public WorldBackdropInput(VisualElement hudBackdrop)
        {
            backdrop = hudBackdrop;
            backdrop.RegisterCallback<PointerDownEvent>(OnPointerDown);
            backdrop.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            backdrop.RegisterCallback<PointerUpEvent>(OnPointerUp);
            backdrop.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            backdrop.RegisterCallback<WheelEvent>(OnWheel);
            longPressItem = backdrop.schedule.Execute(CheckLongPress).Every(LongPressPollMilliseconds);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            longPressItem?.Pause();
            longPressItem = null;
            pointers.Clear();
            pinchEngaged = false;
            backdrop.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            backdrop.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            backdrop.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            backdrop.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            backdrop.UnregisterCallback<WheelEvent>(OnWheel);
        }

        private static SpaceWorldPresenter World => SpaceWorldPresenter.Current;

        private float PixelsPerPoint => backdrop.panel?.scaledPixelsPerPoint ?? 1f;

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!ReferenceEquals(evt.target, backdrop)) return;
            var screenPosition = (Vector2)evt.position * PixelsPerPoint;
            pointers[evt.pointerId] = new PointerTrack
            {
                DownPosition = screenPosition,
                LastPosition = screenPosition,
                DownTime = Time.unscaledTime,
                Button = evt.button,
            };
            longPressFired = false;

            if (pointers.Count == 1) return;

            // Second finger: switch to pinch zoom and cancel pending taps.
            // Fingers beyond the pair are ignored so a palm resting on the
            // screen cannot teleport the camera distance.
            if (pointers.Count == 2)
            {
                pinchEngaged = true;
                LockPinchPair();
                World?.BeginPinchZoom(Mathf.Max(1f, PinchDistance()));
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!pointers.TryGetValue(evt.pointerId, out var track)) return;
            var screenPosition = (Vector2)evt.position * PixelsPerPoint;
            var delta = screenPosition - track.LastPosition;
            track.LastPosition = screenPosition;
            if ((screenPosition - track.DownPosition).sqrMagnitude > TapSlopPixels * TapSlopPixels)
                track.Moved = true;

            if (pointers.Count >= 2)
            {
                if (pinchEngaged && IsPinchFinger(evt.pointerId))
                    World?.UpdatePinchZoom(Mathf.Max(1f, PinchDistance()));
                return;
            }

            // Any pressed pointer drags the camera orbit, mirroring the old
            // right-mouse orbit on desktop.
            World?.OrbitCamera(delta.x, delta.y);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!pointers.TryGetValue(evt.pointerId, out var track)) return;
            pointers.Remove(evt.pointerId);

            if (pinchEngaged)
            {
                if (IsPinchFinger(evt.pointerId))
                {
                    // A pinch finger lifted: re-baseline on the remaining pair
                    // if one exists, otherwise the pinch is over.
                    if (pointers.Count >= 2)
                    {
                        LockPinchPair();
                        World?.BeginPinchZoom(Mathf.Max(1f, PinchDistance()));
                    }
                    else
                    {
                        pinchEngaged = false;
                        pinchPointerA = pinchPointerB = -1;
                    }
                }
                return;
            }
            if (longPressFired || track.Moved) return;

            var screenPosition = (Vector2)evt.position * PixelsPerPoint;

            // Desktop parity: a clean right-click opens the context menu; the
            // touch equivalent is the long-press that fires while held.
            if (track.Button == 1)
            {
                World?.LongPress(screenPosition);
                return;
            }

            if (Time.unscaledTime - track.DownTime > TapMaxDurationSeconds) return;

            var isDoubleTap = Time.unscaledTime - lastTapTime < DoubleTapMaxSeconds &&
                (screenPosition - lastTapPosition).sqrMagnitude < DoubleTapSlopPixels * DoubleTapSlopPixels;
            lastTapTime = Time.unscaledTime;
            lastTapPosition = screenPosition;

            if (isDoubleTap)
            {
                lastTapTime = -10f;
                World?.DoubleTap(screenPosition);
            }
            else
            {
                World?.Tap(screenPosition);
            }
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (!pointers.Remove(evt.pointerId)) return;
            if (pinchEngaged && (pointers.Count < 2 || IsPinchFinger(evt.pointerId)))
            {
                pinchEngaged = false;
                pinchPointerA = pinchPointerB = -1;
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            if (!ReferenceEquals(evt.target, backdrop)) return;
            World?.ZoomWheel(evt.delta.y);
        }

        private void CheckLongPress()
        {
            if (longPressFired || pinchEngaged || pointers.Count != 1) return;
            foreach (var track in pointers.Values)
            {
                if (track.Moved) return;
                if ((Time.unscaledTime - track.DownTime) * 1000f + 1f < LongPressMilliseconds) return;
                longPressFired = true;
                World?.LongPress(track.LastPosition);
                return;
            }
        }

        private bool IsPinchFinger(int pointerId) => pointerId == pinchPointerA || pointerId == pinchPointerB;

        private void LockPinchPair()
        {
            pinchPointerA = -1;
            pinchPointerB = -1;
            foreach (var id in pointers.Keys)
            {
                if (pinchPointerA < 0) pinchPointerA = id;
                else
                {
                    pinchPointerB = id;
                    return;
                }
            }
        }

        private float PinchDistance()
        {
            return pointers.TryGetValue(pinchPointerA, out var a) && pointers.TryGetValue(pinchPointerB, out var b)
                ? Vector2.Distance(a.LastPosition, b.LastPosition)
                : 1f;
        }
    }
}
