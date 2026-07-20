using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Starfall.Presentation
{
    [DisallowMultipleComponent]
    public sealed class SpaceWorldPresenter : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> views = new();
        private readonly Dictionary<string, WorldObjectViewData> dataById = new();
        private readonly List<GameObject> transientVfx = new();
        private readonly WorldGestureRecognizer touchGestures = new();
        private readonly HashSet<string> aliveIds = new(StringComparer.Ordinal);
        private readonly List<string> pendingRemoval = new();
        private SpaceSnapshot snapshot;
        private Transform worldRoot;
        private EveCameraController cameraController;
        private GameObject skyDome;
        private Light keyLight;
        private string presentedSkySystemId = string.Empty;
        private string selectedId = string.Empty;
        private float lastClickTime = -10f;
        private Vector2 lastClickPosition;
        private bool touchInputActive;
        private bool enhancedTouchEnabled;
#if STARFALL_ANDROID_CI
        private const string AndroidCiGestureEvidenceFile = "starfall-ci-gesture.json";
        private GameObject androidCiTouchProxy;
        private string androidCiTouchProxyId = string.Empty;
        private int androidCiGestureGeneration;
#endif

        public event Action<string> SelectionChanged;
        public event Action<Vector3, string> ApproachRequested;
        public event Action<string> ContextRequested;
        public Camera MainCamera => cameraController ? cameraController.Camera : null;

        private void Awake()
        {
            EnsureEnvironment();
            EnableEnhancedTouchInput();
        }

        private void OnEnable() => EnableEnhancedTouchInput();

        private void OnDisable() => DisableEnhancedTouchInput();

        private void Update()
        {
            UpdateTouchInput();
            UpdatePicking();
            AnimateWorld();
            for (var i = transientVfx.Count - 1; i >= 0; i--)
                if (!transientVfx[i]) transientVfx.RemoveAt(i);
        }

        public void Present(SpaceSnapshot next)
        {
            if (next == null) return;
            snapshot = next;
            PresentSystemEnvironment(next);
            dataById.Clear();
            aliveIds.Clear();
            foreach (var data in next.Objects)
            {
                if (string.IsNullOrWhiteSpace(data.Id)) continue;
                aliveIds.Add(data.Id);
                dataById[data.Id] = data;
                if (!views.TryGetValue(data.Id, out var view) || !view)
                {
                    view = CreateView(data);
                    views[data.Id] = view;
                }
                view.transform.position = data.Position;
                if (data.Kind == WorldViewKind.Ship)
                    view.transform.rotation = Quaternion.Euler(0, data.HeadingDegrees, 0);
                view.SetActive(true);
                if (data.IsPlayer) cameraController.SetPlayerTarget(view.transform);
            }

            pendingRemoval.Clear();
            foreach (var pair in views)
            {
                if (aliveIds.Contains(pair.Key)) continue;
                if (pair.Value) Destroy(pair.Value);
                pendingRemoval.Add(pair.Key);
            }
            foreach (var id in pendingRemoval) views.Remove(id);
            ApplySelection(next.SelectedId, false);
        }

        public void Select(string stableId)
        {
            ApplySelection(stableId, true);
        }

        private void ApplySelection(string stableId, bool notify)
        {
            var normalizedId = stableId ?? string.Empty;
            var changed = !string.Equals(selectedId, normalizedId, StringComparison.Ordinal);
            selectedId = normalizedId;
            if (views.TryGetValue(selectedId, out var selected) && selected)
                cameraController.SetSelectedTarget(selected.transform);
            else
                cameraController.SetSelectedTarget(null);
            if (changed && notify) SelectionChanged?.Invoke(selectedId);
        }

        public void FireBeam(string fromId, string toId, Color color)
        {
            if (!views.TryGetValue(fromId, out var from) || !from ||
                !views.TryGetValue(toId, out var to) || !to) return;
            var beam = new GameObject("BeamVFX");
            var line = beam.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, from.transform.position);
            line.SetPosition(1, to.transform.position);
            line.startWidth = 0.16f;
            line.endWidth = 0.04f;
            line.material = ProceduralShipFactory.GetMaterial($"beam-{ColorUtility.ToHtmlStringRGB(color)}", color, 0.1f, 0f, true);
            transientVfx.Add(beam);
            Destroy(beam, 0.12f);
        }

        public void Explosion(Vector3 position, Color color, float size = 5f)
        {
            var root = new GameObject("ExplosionVFX");
            root.transform.position = position;
            var ps = root.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.7f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.1f, size * 0.35f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 45) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = size * 0.18f;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ProceduralShipFactory.GetMaterial("explosion", new Color(1f, 0.24f, 0.03f), 0f, 0f, true);
            transientVfx.Add(root);
            Destroy(root, 1.5f);
        }

        public int ReleaseTransientResources()
        {
            var released = 0;
            for (var i = transientVfx.Count - 1; i >= 0; i--)
            {
                if (!transientVfx[i]) continue;
                Destroy(transientVfx[i]);
                released++;
            }
            transientVfx.Clear();
            return released;
        }

#if STARFALL_ANDROID_CI
        public int SeedAndroidCiLowMemoryFixture()
        {
            var fixture = new GameObject("AndroidCiLowMemoryFixture");
            fixture.hideFlags = HideFlags.DontSave;
            transientVfx.Add(fixture);
            return 1;
        }

        public float AndroidCiCameraYawDegrees => cameraController
            ? cameraController.AndroidCiYawDegrees
            : float.NaN;

        public float AndroidCiCameraDistance => cameraController
            ? cameraController.AndroidCiDistance
            : float.NaN;

        public string PrepareAndroidCiTouchTarget()
        {
            if (!cameraController || !cameraController.Camera) return string.Empty;
            if (!TryGetAndroidCiDragPath(out var clearPoint, out _)) return string.Empty;
            foreach (var pair in views)
            {
                var view = pair.Value;
                if (!view || !dataById.TryGetValue(pair.Key, out var data) || data.IsPlayer) continue;

                if (androidCiTouchProxy) Destroy(androidCiTouchProxy);
                androidCiTouchProxy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                androidCiTouchProxy.name = "AndroidCiTouchProxy";
                androidCiTouchProxy.hideFlags = HideFlags.DontSave;
                androidCiTouchProxy.transform.SetParent(worldRoot, false);
                androidCiTouchProxy.transform.position = cameraController.Camera
                    .ScreenPointToRay(clearPoint).GetPoint(8f);
                androidCiTouchProxy.transform.localScale = Vector3.one * 0.9f;
                androidCiTouchProxy.GetComponent<Renderer>().sharedMaterial =
                    ProceduralShipFactory.GetMaterial("android-ci-touch-proxy", data.Color, 0.2f, 0f, true);
                androidCiTouchProxy.AddComponent<SelectableView>().Configure(data);
                androidCiTouchProxyId = pair.Key;
                Physics.SyncTransforms();
                return pair.Key;
            }
            return string.Empty;
        }

        public bool TryGetAndroidCiTouchTarget(out string stableId, out Vector2 screenPosition)
        {
            stableId = string.Empty;
            screenPosition = default;
            if (!cameraController || !cameraController.Camera) return false;

            Physics.SyncTransforms();
            if (androidCiTouchProxy && TryResolveAndroidCiTouchView(
                    androidCiTouchProxy, androidCiTouchProxyId, out screenPosition))
            {
                stableId = androidCiTouchProxyId;
                return true;
            }
            foreach (var pair in views)
            {
                var view = pair.Value;
                if (!view || !dataById.TryGetValue(pair.Key, out var data) || data.IsPlayer) continue;
                if (!TryResolveAndroidCiTouchView(view, pair.Key, out var candidate)) continue;
                stableId = pair.Key;
                screenPosition = candidate;
                return true;
            }
            return false;
        }

        private bool TryResolveAndroidCiTouchView(
            GameObject view, string stableId, out Vector2 screenPosition)
        {
            screenPosition = default;
            var projected = cameraController.Camera.WorldToScreenPoint(view.transform.position);
            var candidate = new Vector2(projected.x, projected.y);
            if (projected.z <= 0f || !IsInsideScreen(candidate) || WorldPointerBlocker.Blocks(candidate))
                return false;
            var ray = cameraController.Camera.ScreenPointToRay(candidate);
            if (!Physics.Raycast(ray, out var hit, 15000f)) return false;
            var selectable = hit.collider.GetComponentInParent<SelectableView>();
            if (!selectable || !string.Equals(selectable.StableId, stableId, StringComparison.Ordinal))
                return false;
            screenPosition = candidate;
            return true;
        }

        public bool TryGetAndroidCiDragPath(out Vector2 start, out Vector2 end)
        {
            start = default;
            end = default;
            if (!cameraController || !cameraController.Camera) return false;
            var width = Mathf.Max(1f, Screen.width);
            var height = Mathf.Max(1f, Screen.height);
            var delta = new Vector2(Mathf.Max(96f, width * 0.08f), Mathf.Max(48f, height * 0.06f));
            foreach (var normalized in new[]
                     {
                         new Vector2(0.50f, 0.52f), new Vector2(0.58f, 0.46f),
                         new Vector2(0.43f, 0.42f), new Vector2(0.52f, 0.66f),
                     })
            {
                var candidateStart = new Vector2(width * normalized.x, height * normalized.y);
                var candidateEnd = candidateStart + delta;
                if (!IsBlankWorldPoint(candidateStart) || !IsBlankWorldPoint(candidateEnd)) continue;
                start = candidateStart;
                end = candidateEnd;
                return true;
            }

            // Preparing the physical-touch target deliberately occupies the first clear
            // ray with a selectable proxy. Search both foldable panes for a second path,
            // keeping every drag on its own side of a possible central vertical hinge.
            foreach (var normalizedY in new[] { 0.35f, 0.50f, 0.65f, 0.80f })
            foreach (var normalizedX in new[] { 0.18f, 0.30f, 0.42f, 0.58f, 0.70f, 0.82f })
            {
                var candidateStart = new Vector2(width * normalizedX, height * normalizedY);
                var horizontalDirection = normalizedX < 0.5f ? -1f : 1f;
                var verticalDirection = normalizedY < 0.5f ? 1f : -1f;
                var candidateEnd = candidateStart + new Vector2(
                    delta.x * horizontalDirection, delta.y * verticalDirection);
                if (!IsBlankWorldPoint(candidateStart) || !IsBlankWorldPoint(candidateEnd)) continue;
                start = candidateStart;
                end = candidateEnd;
                return true;
            }
            return false;
        }

        private bool IsBlankWorldPoint(Vector2 position)
        {
            return IsInsideScreen(position) && !WorldPointerBlocker.Blocks(position) && !HitsSelectable(position);
        }

        private static bool IsInsideScreen(Vector2 position)
        {
            const float margin = 8f;
            return position.x >= margin && position.y >= margin &&
                   position.x <= Screen.width - margin && position.y <= Screen.height - margin;
        }
#endif

        private void EnsureEnvironment()
        {
            worldRoot = new GameObject("GeneratedWorld").transform;
            worldRoot.SetParent(transform, false);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraController = cameraObject.AddComponent<EveCameraController>();

            var lightObject = new GameObject("Key Light");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(32f, -38f, 0);
            keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.intensity = 1.15f;
            keyLight.color = new Color(0.72f, 0.82f, 1f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.025f, 0.055f, 0.12f);
            RenderSettings.ambientEquatorColor = new Color(0.015f, 0.025f, 0.055f);
            RenderSettings.ambientGroundColor = new Color(0.004f, 0.006f, 0.014f);
            RenderSettings.ambientIntensity = 0.9f;
            // Station uses depth fog, but RenderSettings is global and can survive
            // a scene transition. Space must always restore a clear deep-space view.
            RenderSettings.fog = false;

            CreatePostProcessing();
        }

        private void PresentSystemEnvironment(SpaceSnapshot next)
        {
            if (string.Equals(presentedSkySystemId, next.SystemId, StringComparison.Ordinal)) return;

            if (skyDome)
            {
                skyDome.SetActive(false);
                Destroy(skyDome);
            }

            var style = ProceduralSpaceMaterials.GetSystemSkyStyle(
                next.SystemId, next.FactionId, next.FactionColor, next.Security);
            skyDome = ProceduralSpaceMaterials.CreateSystemSkyDome(cameraController.transform, style);
            presentedSkySystemId = next.SystemId;

            RenderSettings.ambientSkyColor = style.AmbientSky;
            RenderSettings.ambientEquatorColor = style.AmbientEquator;
            RenderSettings.ambientGroundColor = style.Background * 0.38f;
            if (keyLight) keyLight.color = style.KeyLight;
        }

        private void CreatePostProcessing()
        {
            var volumeObject = new GameObject("Global Volume");
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>();
            bloom.active = true;
            bloom.intensity.Override(0.62f);
            bloom.threshold.Override(0.92f);
            bloom.scatter.Override(0.66f);
            var vignette = profile.Add<Vignette>();
            vignette.active = true;
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.7f);
            var tone = profile.Add<Tonemapping>();
            tone.active = true;
            tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>();
            color.contrast.Override(7f);
            color.saturation.Override(-3f);
            volume.profile = profile;
        }

        private GameObject CreateView(WorldObjectViewData data)
        {
            GameObject view;
            switch (data.Kind)
            {
                case WorldViewKind.Ship:
                    view = ProceduralShipFactory.CreateShip(data.ShipId, data.ShipClass, data.Color, data.IsHostile);
                    break;
                case WorldViewKind.Station:
                    view = ProceduralShipFactory.CreateStation(data.Color);
                    break;
                case WorldViewKind.Gate:
                    view = ProceduralShipFactory.CreateGate(data.Color);
                    break;
                case WorldViewKind.Star:
                    view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    view.transform.localScale = Vector3.one * data.Radius * 2f;
                    view.GetComponent<Renderer>().sharedMaterial = ProceduralShipFactory.GetMaterial(
                        $"star-{ColorUtility.ToHtmlStringRGB(data.Color)}", data.Color, 0.1f, 0f, true);
                    var point = view.AddComponent<Light>();
                    point.type = LightType.Point;
                    point.color = data.Color;
                    point.intensity = 4f;
                    point.range = Mathf.Max(500f, data.Radius * 30f);
                    break;
                case WorldViewKind.Planet:
                case WorldViewKind.Moon:
                    view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    view.transform.localScale = Vector3.one * data.Radius * 2f;
                    view.GetComponent<Renderer>().sharedMaterial = ProceduralSpaceMaterials.GetPlanetMaterial(
                        data.Id, data.Color, data.Kind == WorldViewKind.Moon);
                    break;
                case WorldViewKind.Asteroid:
                    view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    view.transform.localScale = new Vector3(data.Radius * 1.7f, data.Radius, data.Radius * 1.25f);
                    view.transform.rotation = Quaternion.Euler(data.Radius * 9f, data.Radius * 17f, data.Radius * 4f);
                    view.GetComponent<Renderer>().sharedMaterial = ProceduralSpaceMaterials.GetAsteroidMaterial();
                    break;
                default:
                    view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    view.transform.localScale = Vector3.one * Mathf.Max(0.6f, data.Radius);
                    view.GetComponent<Renderer>().sharedMaterial = ProceduralShipFactory.GetMaterial(
                        $"object-{ColorUtility.ToHtmlStringRGB(data.Color)}", data.Color, 0.4f, 0.1f, data.Kind == WorldViewKind.Beacon);
                    break;
            }
            view.name = $"{data.Kind}_{data.Id}";
            view.transform.SetParent(worldRoot, false);
            view.transform.position = data.Position;
            var selectable = view.AddComponent<SelectableView>();
            selectable.Configure(data);
            return view;
        }

        private void UpdatePicking()
        {
            var mouse = Mouse.current;
            if (touchInputActive || mouse == null || !cameraController || !cameraController.Camera) return;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                var position = mouse.position.ReadValue();
                if (WorldPointerBlocker.Blocks(position)) return;
                var ray = cameraController.Camera.ScreenPointToRay(position);
                if (Physics.Raycast(ray, out var hit, 15000f))
                {
                    var selectable = hit.collider.GetComponentInParent<SelectableView>();
                    if (selectable) Select(selectable.StableId);
                }
                var isDouble = Time.unscaledTime - lastClickTime < 0.32f && Vector2.Distance(position, lastClickPosition) < 12f;
                if (isDouble)
                {
                    var plane = new Plane(Vector3.up, Vector3.zero);
                    if (plane.Raycast(ray, out var enter))
                        ApproachRequested?.Invoke(ray.GetPoint(enter), selectedId);
                }
                lastClickTime = Time.unscaledTime;
                lastClickPosition = position;
            }
            if (mouse.rightButton.wasReleasedThisFrame && mouse.delta.ReadValue().sqrMagnitude < 12f)
            {
                var position = mouse.position.ReadValue();
                if (WorldPointerBlocker.Blocks(position)) return;
                var ray = cameraController.Camera.ScreenPointToRay(position);
                if (Physics.Raycast(ray, out var hit, 15000f))
                {
                    var selectable = hit.collider.GetComponentInParent<SelectableView>();
                    if (selectable)
                    {
                        Select(selectable.StableId);
                        ContextRequested?.Invoke(selectable.StableId);
                    }
                }
            }
        }

        private void UpdateTouchInput()
        {
            touchGestures.ConfigureDpi(Screen.dpi);
            if (Touchscreen.current == null && touchGestures.ActivePointerCount > 0)
            {
                touchGestures.Reset();
            }
            DispatchGesture(touchGestures.Tick(Time.unscaledTimeAsDouble));
            touchInputActive = touchGestures.ActivePointerCount > 0;
        }

        private void EnableEnhancedTouchInput()
        {
            if (enhancedTouchEnabled) return;
            EnhancedTouchSupport.Enable();
            EnhancedTouch.onFingerDown += OnFingerDown;
            EnhancedTouch.onFingerMove += OnFingerMove;
            EnhancedTouch.onFingerUp += OnFingerUp;
            enhancedTouchEnabled = true;
        }

        private void DisableEnhancedTouchInput()
        {
            if (!enhancedTouchEnabled) return;
            EnhancedTouch.onFingerDown -= OnFingerDown;
            EnhancedTouch.onFingerMove -= OnFingerMove;
            EnhancedTouch.onFingerUp -= OnFingerUp;
            EnhancedTouchSupport.Disable();
            touchGestures.Reset();
            touchInputActive = false;
            enhancedTouchEnabled = false;
        }

        private void OnFingerDown(Finger finger)
        {
            var touch = finger.currentTouch;
            if (!touch.valid) return;
            touchGestures.ConfigureDpi(Screen.dpi);
            var position = touch.screenPosition;
            touchGestures.Begin(finger.index, position, touch.startTime,
                WorldPointerBlocker.Blocks(position), !HitsSelectable(position));
            touchInputActive = true;
        }

        private void OnFingerMove(Finger finger)
        {
            var touch = finger.currentTouch;
            if (!touch.valid) return;
            DispatchGesture(touchGestures.Move(
                finger.index, touch.screenPosition, touch.time));
            touchInputActive = touchGestures.ActivePointerCount > 0;
        }

        private void OnFingerUp(Finger finger)
        {
            var touch = finger.currentTouch;
            if (!touch.valid)
            {
                touchGestures.Cancel(finger.index);
                touchInputActive = touchGestures.ActivePointerCount > 0;
                return;
            }
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
            {
                touchGestures.Cancel(finger.index);
            }
            else
            {
                // EnhancedTouch records every state change, including touches
                // that begin and end between two slow render frames. Device
                // start time keeps double-tap cadence independent of FPS.
                var tapTime = touch.startTime;
                DispatchGesture(touchGestures.End(
                    finger.index, touch.screenPosition, tapTime), tapTime);
            }
            touchInputActive = touchGestures.ActivePointerCount > 0;
        }

        private void DispatchGesture(WorldGestureEvent? gesture, double? inputStartTime = null)
        {
            if (!gesture.HasValue || !cameraController || !cameraController.Camera) return;
            var value = gesture.Value;
#if STARFALL_ANDROID_CI
            WriteAndroidCiGestureEvidence(value.Type, inputStartTime);
#endif
            switch (value.Type)
            {
                case WorldGestureType.Tap:
                    SelectAt(value.Position, false);
                    break;
                case WorldGestureType.DoubleTap:
                    ApproachAt(value.Position);
                    break;
                case WorldGestureType.Drag:
                    cameraController.ApplyOrbit(value.Delta);
                    break;
                case WorldGestureType.Pinch:
                    cameraController.ApplyZoom(value.Scale);
                    break;
                case WorldGestureType.LongPress:
                    SelectAt(value.Position, true);
                    break;
            }
        }

#if STARFALL_ANDROID_CI
        private void WriteAndroidCiGestureEvidence(
            WorldGestureType gestureType,
            double? inputStartTime)
        {
            try
            {
                androidCiGestureGeneration++;
                var inputStartTimeJson = inputStartTime.HasValue &&
                                         !double.IsNaN(inputStartTime.Value) &&
                                         !double.IsInfinity(inputStartTime.Value)
                    ? inputStartTime.Value.ToString(
                        "R", System.Globalization.CultureInfo.InvariantCulture)
                    : "null";
                var payload = "{\"generation\":" + androidCiGestureGeneration +
                              ",\"gesture\":\"" + gestureType +
                              "\",\"frameCount\":" + Time.frameCount +
                              ",\"inputStartTime\":" + inputStartTimeJson +
                              ",\"unscaledTime\":" +
                              Time.unscaledTimeAsDouble.ToString(
                                  "R", System.Globalization.CultureInfo.InvariantCulture) + "}";
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(Application.persistentDataPath,
                        AndroidCiGestureEvidenceFile),
                    payload);
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(Application.persistentDataPath,
                        "starfall-ci-gesture-" + androidCiGestureGeneration + ".json"),
                    payload);
            }
            catch (Exception exception)
            {
                Debug.LogError("STARFALL_ANDROID_CI_GESTURE_ERR=" + exception.Message);
            }
        }
#endif

        private void SelectAt(Vector2 screenPosition, bool requestContext)
        {
            var ray = cameraController.Camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, 15000f)) return;
            var selectable = hit.collider.GetComponentInParent<SelectableView>();
            if (!selectable) return;
            Select(selectable.StableId);
            if (requestContext) ContextRequested?.Invoke(selectable.StableId);
        }

        private bool HitsSelectable(Vector2 screenPosition)
        {
            if (!cameraController || !cameraController.Camera) return false;
            var ray = cameraController.Camera.ScreenPointToRay(screenPosition);
            return Physics.Raycast(ray, out var hit, 15000f) &&
                   hit.collider.GetComponentInParent<SelectableView>();
        }

        private void ApproachAt(Vector2 screenPosition)
        {
            var ray = cameraController.Camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out var enter))
                ApproachRequested?.Invoke(ray.GetPoint(enter), selectedId);
        }

        private void AnimateWorld()
        {
            foreach (var pair in views)
            {
                if (!dataById.TryGetValue(pair.Key, out var data) || !pair.Value) continue;
                if (data.Kind is WorldViewKind.Planet or WorldViewKind.Moon or WorldViewKind.Station or WorldViewKind.Gate)
                    pair.Value.transform.Rotate(Vector3.up, Time.deltaTime * (data.Kind == WorldViewKind.Station ? 2.2f : 0.8f), Space.World);
            }
        }
    }
}
