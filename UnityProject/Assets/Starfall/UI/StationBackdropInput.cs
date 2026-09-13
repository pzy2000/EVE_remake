using Starfall.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starfall.UI
{
    /// <summary>
    /// Translates pointer gestures on the station screen background into hangar
    /// camera moves: one-finger drag orbits the displayed ship, two-finger pinch
    /// or wheel zooms. Panels and buttons sit above the backdrop and keep their
    /// own events, so gestures only reach the hangar where no UI element is
    /// under the finger — the same contract as WorldBackdropInput in space.
    /// </summary>
    public sealed class StationBackdropInput
    {
        private readonly VisualElement backdrop;
        private readonly System.Collections.Generic.Dictionary<int, PointerTrack> pointers =
            new System.Collections.Generic.Dictionary<int, PointerTrack>();

        private bool pinchEngaged;
        private bool disposed;
        // The two fingers driving the pinch; extra touches (palm, third finger)
        // must not teleport the camera distance.
        private int pinchPointerA = -1;
        private int pinchPointerB = -1;

        private sealed class PointerTrack
        {
            public Vector2 LastPosition;
        }

        public StationBackdropInput(VisualElement stationBackdrop)
        {
            backdrop = stationBackdrop;
            backdrop.RegisterCallback<PointerDownEvent>(OnPointerDown);
            backdrop.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            backdrop.RegisterCallback<PointerUpEvent>(OnPointerUp);
            backdrop.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            backdrop.RegisterCallback<WheelEvent>(OnWheel);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            backdrop.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            backdrop.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            backdrop.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            backdrop.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
            backdrop.UnregisterCallback<WheelEvent>(OnWheel);
        }

        private static StationHangarPresenter Presenter => StationHangarPresenter.Current;

        private float PixelsPerPoint => backdrop.panel?.scaledPixelsPerPoint ?? 1f;

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (disposed || !ReferenceEquals(evt.target, backdrop)) return;
            pointers[evt.pointerId] = new PointerTrack
            {
                LastPosition = (Vector2)evt.position * PixelsPerPoint,
            };

            if (pointers.Count == 2)
            {
                pinchEngaged = true;
                LockPinchPair();
                Presenter?.BeginPinchZoom(Mathf.Max(1f, PinchDistance()));
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (disposed || !pointers.TryGetValue(evt.pointerId, out var track)) return;
            var screenPosition = (Vector2)evt.position * PixelsPerPoint;
            var delta = screenPosition - track.LastPosition;
            track.LastPosition = screenPosition;

            if (pointers.Count >= 2)
            {
                if (pinchEngaged && IsPinchFinger(evt.pointerId))
                    Presenter?.UpdatePinchZoom(Mathf.Max(1f, PinchDistance()));
                return;
            }

            Presenter?.Orbit(delta.x, delta.y);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (disposed || !pointers.Remove(evt.pointerId)) return;
            if (pinchEngaged && IsPinchFinger(evt.pointerId))
            {
                // Re-baseline on whatever pair remains, mirroring the space view.
                if (pointers.Count >= 2)
                {
                    LockPinchPair();
                    Presenter?.BeginPinchZoom(Mathf.Max(1f, PinchDistance()));
                }
                else
                {
                    pinchEngaged = false;
                    pinchPointerA = pinchPointerB = -1;
                }
            }
            if (pointers.Count == 0) pinchEngaged = false;
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (disposed || !pointers.Remove(evt.pointerId)) return;
            if (pinchEngaged && (pointers.Count < 2 || IsPinchFinger(evt.pointerId)))
            {
                pinchEngaged = false;
                pinchPointerA = pinchPointerB = -1;
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            if (disposed || !ReferenceEquals(evt.target, backdrop)) return;
            Presenter?.Zoom(evt.delta.y);
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
                ? UnityEngine.Vector2.Distance(a.LastPosition, b.LastPosition)
                : 1f;
        }
    }
}
