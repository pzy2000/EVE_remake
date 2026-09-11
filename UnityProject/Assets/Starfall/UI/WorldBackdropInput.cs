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

        private readonly VisualElement backdrop;
        private readonly Dictionary<int, PointerTrack> pointers = new Dictionary<int, PointerTrack>();

        private IVisualElementScheduledItem longPressItem;
        private bool longPressFired;
        private bool pinchEngaged;
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

            if (pointers.Count == 1)
            {
                longPressItem?.Pause();
                longPressItem = backdrop.schedule.Execute(OnLongPress).StartingIn(LongPressMilliseconds);
                return;
            }

            // Second finger: switch to pinch zoom and cancel pending taps.
            longPressItem?.Pause();
            pinchEngaged = true;
            if (pointers.Count == 2) World?.BeginPinchZoom(Mathf.Max(1f, PairwiseDistance()));
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!pointers.TryGetValue(evt.pointerId, out var track)) return;
            var screenPosition = (Vector2)evt.position * PixelsPerPoint;
            var delta = screenPosition - track.LastPosition;
            track.LastPosition = screenPosition;
            if ((screenPosition - track.DownPosition).sqrMagnitude > TapSlopPixels * TapSlopPixels)
            {
                track.Moved = true;
                longPressItem?.Pause();
            }

            if (pointers.Count >= 2)
            {
                if (pinchEngaged) World?.UpdatePinchZoom(Mathf.Max(1f, PairwiseDistance()));
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
            longPressItem?.Pause();

            if (pinchEngaged)
            {
                if (pointers.Count == 0) pinchEngaged = false;
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
            longPressItem?.Pause();
            if (pointers.Count == 0) pinchEngaged = false;
        }

        private void OnWheel(WheelEvent evt)
        {
            if (!ReferenceEquals(evt.target, backdrop)) return;
            World?.ZoomWheel(evt.delta.y);
        }

        private void OnLongPress()
        {
            longPressItem?.Pause();
            if (pinchEngaged || pointers.Count != 1) return;
            foreach (var track in pointers.Values)
            {
                if (track.Moved) return;
                longPressFired = true;
                World?.LongPress(track.LastPosition);
                return;
            }
        }

        private float PairwiseDistance()
        {
            var first = true;
            var a = Vector2.zero;
            var b = Vector2.zero;
            foreach (var track in pointers.Values)
            {
                if (first)
                {
                    a = track.LastPosition;
                    first = false;
                }
                else
                {
                    b = track.LastPosition;
                }
            }
            return Vector2.Distance(a, b);
        }
    }
}
