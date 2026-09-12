using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Starfall.Presentation
{
    [DisallowMultipleComponent]
    public sealed class SpaceWorldPresenter : MonoBehaviour
    {
        private readonly Dictionary<string, GameObject> views = new();
        private readonly Dictionary<string, WorldObjectViewData> dataById = new();
        private readonly HashSet<string> aliveIds = new(StringComparer.Ordinal);
        private readonly List<string> pendingRemoval = new();
        // Beams and explosions used to be built from scratch per event, which
        // turned sustained combat into an allocation storm on mid phones.
        private readonly Stack<LineRenderer> beamPool = new();
        private readonly Stack<ParticleSystem> explosionPool = new();
        private readonly List<TimedVfx> activeVfx = new();
        private SpaceSnapshot snapshot;
        private Transform worldRoot;
        private Transform vfxRoot;
        private EveCameraController cameraController;
        private GameObject skyDome;
        private Light keyLight;
        private VolumeProfile postProfile;
        private string presentedSkySystemId = string.Empty;
        private string selectedId = string.Empty;

        private struct TimedVfx
        {
            public GameObject Root;
            public float ExpireAt;
            public LineRenderer Beam;
            public ParticleSystem Burst;
        }

        // World gestures arrive through the HUD backdrop (see WorldBackdropInput);
        // this scene-scoped handle lets the HUD find the presenter without wiring.
        public static SpaceWorldPresenter Current { get; private set; }

        public event Action<string> SelectionChanged;
        public event Action<Vector3, string> ApproachRequested;
        public event Action<string> ContextRequested;
        public Camera MainCamera => cameraController ? cameraController.Camera : null;

        private void Awake()
        {
            EnsureEnvironment();
        }

        private void OnEnable()
        {
            Current = this;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        private void Update()
        {
            AnimateWorld();
            RecycleVfx();
        }

        private void OnDestroy()
        {
            // Runtime-created profiles survive scene unloads; without this every
            // dock/jump cycle leaks one VolumeProfile.
            if (postProfile) Destroy(postProfile);
        }

        private void RecycleVfx()
        {
            for (var i = activeVfx.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < activeVfx[i].ExpireAt) continue;
                var vfx = activeVfx[i];
                activeVfx.RemoveAt(i);
                if (!vfx.Root) continue;
                vfx.Root.SetActive(false);
                if (vfx.Beam != null) beamPool.Push(vfx.Beam);
                if (vfx.Burst != null) explosionPool.Push(vfx.Burst);
            }
        }

        /// <summary>Camera controller passthrough so the HUD can drive the view.</summary>
        public EveCameraController CameraController => cameraController;

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
            var line = beamPool.Count > 0 ? beamPool.Pop() : CreateBeam();
            line.gameObject.SetActive(true);
            line.SetPosition(0, from.transform.position);
            line.SetPosition(1, to.transform.position);
            line.material = ProceduralShipFactory.GetMaterial($"beam-{ColorUtility.ToHtmlStringRGB(color)}", color, 0.1f, 0f, true);
            activeVfx.Add(new TimedVfx { Root = line.gameObject, ExpireAt = Time.unscaledTime + 0.12f, Beam = line });
        }

        private LineRenderer CreateBeam()
        {
            var beam = new GameObject("BeamVFX");
            beam.transform.SetParent(vfxRoot, false);
            var line = beam.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.startWidth = 0.16f;
            line.endWidth = 0.04f;
            return line;
        }

        public void Explosion(Vector3 position, Color color, float size = 5f)
        {
            var burst = explosionPool.Count > 0 ? explosionPool.Pop() : CreateExplosionSystem();
            burst.gameObject.SetActive(true);
            burst.transform.position = position;
            var main = burst.main;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white, color);
            main.startSpeed = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.1f, size * 0.35f);
            var shape = burst.shape;
            shape.radius = size * 0.18f;
            burst.Play();
            activeVfx.Add(new TimedVfx { Root = burst.gameObject, ExpireAt = Time.unscaledTime + 1.5f, Burst = burst });
        }

        /// <summary>Impact feedback on the player's hull: cold sparks while shields hold, hot when bleeding.</summary>
        public void PlayerImpact(Vector3 position, bool shieldsHeld)
        {
            // Small and short; combat already fires beams, this is the "you are
            // being shot" cue that used to be completely invisible.
            Explosion(position, shieldsHeld
                ? new Color(0.42f, 0.78f, 1f)
                : new Color(1f, 0.45f, 0.12f), 1.7f);
        }

        /// <summary>Warp/jump acceleration feedback: FOV punch plus a drive flash at the ship.</summary>
        public void WarpFlash(Vector3 position)
        {
            if (cameraController) cameraController.PunchFieldOfView(8f);
            Explosion(position, new Color(0.45f, 0.85f, 1f), 3.2f);
        }

        private ParticleSystem CreateExplosionSystem()
        {
            var root = new GameObject("ExplosionVFX");
            root.transform.SetParent(vfxRoot, false);
            var ps = root.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 0.7f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.85f);
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, 45) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ProceduralShipFactory.GetMaterial("explosion", new Color(1f, 0.24f, 0.03f), 0f, 0f, true);
            return ps;
        }

        private void EnsureEnvironment()
        {
            worldRoot = new GameObject("GeneratedWorld").transform;
            worldRoot.SetParent(transform, false);
            vfxRoot = new GameObject("VfxRoot").transform;
            vfxRoot.SetParent(transform, false);

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
            var profile = postProfile = ScriptableObject.CreateInstance<VolumeProfile>();
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
                    AttachEngineTrail(view, data);
                    break;
                case WorldViewKind.Station:
                    view = ProceduralShipFactory.CreateStation(data.Color);
                    break;
                case WorldViewKind.Gate:
                    view = ProceduralShipFactory.CreateGate(data.Color);
                    break;
                case WorldViewKind.Star:
                    // The star sphere is fully emissive; a per-star pixel light
                    // is pure extra-pixel-light cost on mobile for no visual gain.
                    view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    view.transform.localScale = Vector3.one * data.Radius * 2f;
                    view.GetComponent<Renderer>().sharedMaterial = ProceduralShipFactory.GetMaterial(
                        $"star-{ColorUtility.ToHtmlStringRGB(data.Color)}", data.Color, 0.1f, 0f, true);
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

        /// <summary>
        /// Engine wake so moving ships read as powered vehicles instead of sliding
        /// toys. The trail only emits when the transform actually moves, so idle
        /// ships cost nothing.
        /// </summary>
        private static void AttachEngineTrail(GameObject shipView, WorldObjectViewData data)
        {
            if (data.IsPlayer == false && data.Radius < 5f) return; // small NPCs skip the cost
            var trail = shipView.AddComponent<TrailRenderer>();
            trail.time = 0.55f;
            trail.startWidth = Mathf.Max(0.12f, data.Radius * 0.14f);
            trail.endWidth = 0.015f;
            trail.minVertexDistance = 0.6f;
            trail.numCapVertices = 2;
            trail.autodestruct = false;
            trail.emitting = true;
            trail.material = ProceduralShipFactory.GetMaterial(
                $"trail-{ColorUtility.ToHtmlStringRGB(data.Color)}", data.Color * 1.5f, 0.1f, 0f, true);
        }

        #region World gestures (touch + mouse via WorldBackdropInput)

        private bool CanPick => cameraController && cameraController.Camera;

        public void OrbitCamera(float deltaX, float deltaY)
        {
            if (cameraController) cameraController.AddOrbitInput(deltaX, deltaY);
        }

        public void BeginPinchZoom(float startDistancePixels)
        {
            if (cameraController) cameraController.BeginPinchZoom(startDistancePixels);
        }

        public void UpdatePinchZoom(float currentDistancePixels)
        {
            if (cameraController) cameraController.UpdatePinchZoom(currentDistancePixels);
        }

        public void ZoomWheel(float wheelDelta)
        {
            if (cameraController) cameraController.AddZoomInput(wheelDelta);
        }

        public void Tap(Vector2 screenPosition)
        {
            if (!CanPick) return;
            if (RaycastSelectable(screenPosition, out var selectable))
                Select(selectable.StableId);
        }

        public void DoubleTap(Vector2 screenPosition)
        {
            if (!CanPick) return;
            var ray = cameraController.Camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out var enter))
                ApproachRequested?.Invoke(ray.GetPoint(enter), selectedId);
        }

        public void LongPress(Vector2 screenPosition)
        {
            if (!CanPick) return;
            if (RaycastSelectable(screenPosition, out var selectable))
            {
                Select(selectable.StableId);
                ContextRequested?.Invoke(selectable.StableId);
            }
        }

        private bool RaycastSelectable(Vector2 screenPosition, out SelectableView selectable)
        {
            selectable = null;
            var ray = cameraController.Camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, 15000f)) return false;
            selectable = hit.collider.GetComponentInParent<SelectableView>();
            return selectable != null;
        }

        #endregion

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
