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
                Presenter?.BeginPinchZoom(Mathf.Max(1f, PairwiseDistance()));
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
                if (pinchEngaged) Presenter?.UpdatePinchZoom(Mathf.Max(1f, PairwiseDistance()));
                return;
            }

            Presenter?.Orbit(delta.x, delta.y);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (disposed || !pointers.Remove(evt.pointerId)) return;
            if (pointers.Count == 0) pinchEngaged = false;
        }

        private void OnPointerCancel(PointerCancelEvent evt)
        {
            if (disposed || !pointers.Remove(evt.pointerId)) return;
            if (pointers.Count == 0) pinchEngaged = false;
        }

        private void OnWheel(WheelEvent evt)
        {
            if (disposed || !ReferenceEquals(evt.target, backdrop)) return;
            Presenter?.Zoom(evt.delta.y);
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
