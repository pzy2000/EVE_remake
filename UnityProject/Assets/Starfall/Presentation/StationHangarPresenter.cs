using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Starfall.Presentation
{
    public sealed class StationHangarPresenter : MonoBehaviour
    {
        private static readonly Vector3 PadCenter = new(-2.2f, 0f, 2.2f);
        private static readonly Vector3 ShipDisplayCenter = new(-2.2f, 2.35f, 2.2f);

        // Orbit rig around the displayed ship. Yaw/pitch/distance are clamped to
        // keep the camera inside the hangar mouth — the interior is a small box
        // and an unrestricted orbit swings the camera through walls and roof.
        private const float MaxOrbitYawSwing = 45f;
        private const float MinOrbitPitch = 3f;
        private const float MaxOrbitPitch = 20f;
        private const float MinDistance = 11f;
        private const float MaxDistance = 28f;

        [SerializeField] private string shipId = "acolyte";
        [SerializeField] private string shipClass = "frigate";
        [SerializeField] private Color factionColor = new(1f, 0.66f, 0.2f);

        private Transform ship;
        private Camera hangarCamera;
        private float orbitYaw;
        private float orbitYawBase;
        private float orbitPitch = 12f;
        private float orbitDistance = 20f;
        private float pinchStartPixelDistance = 1f;
        private float pinchStartDistance;
        private Material accentMaterial;
        private Light rimLight;
        private Light fillLight;

        /// <summary>Scene-scoped handle so the station UI can find this rig without wiring.</summary>
        public static StationHangarPresenter Current { get; private set; }

        private void Awake()
        {
            BuildHangar();
        }

        private void OnEnable()
        {
            Current = this;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
        }

        /// <summary>Screen-pixel drag orbit; fed by StationBackdropInput.</summary>
        public void Orbit(float deltaX, float deltaY)
        {
            orbitYaw = Mathf.Clamp(orbitYaw + deltaX * 0.18f,
                orbitYawBase - MaxOrbitYawSwing, orbitYawBase + MaxOrbitYawSwing);
            orbitPitch = Mathf.Clamp(orbitPitch - deltaY * 0.14f, MinOrbitPitch, MaxOrbitPitch);
        }

        public void Zoom(float wheelDelta)
        {
            if (Mathf.Abs(wheelDelta) < 0.01f) return;
            orbitDistance = Mathf.Clamp(orbitDistance * Mathf.Exp(-wheelDelta * 0.008f), MinDistance, MaxDistance);
        }

        public void BeginPinchZoom(float startPixelDistance)
        {
            pinchStartPixelDistance = Mathf.Max(1f, startPixelDistance);
            pinchStartDistance = orbitDistance;
        }

        public void UpdatePinchZoom(float currentPixelDistance)
        {
            var scale = pinchStartPixelDistance / Mathf.Max(1f, currentPixelDistance);
            orbitDistance = Mathf.Clamp(pinchStartDistance * scale, MinDistance, MaxDistance);
        }

        private void LateUpdate()
        {
            if (!hangarCamera) return;
            // Same orbit math as the space camera: position on a sphere around
            // the display center, always looking at it.
            var rotation = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            var desired = transform.TransformPoint(ShipDisplayCenter) + rotation * new Vector3(0f, 0f, -orbitDistance);
            hangarCamera.transform.position = desired;
            hangarCamera.transform.rotation = Quaternion.LookRotation(
                transform.TransformPoint(ShipDisplayCenter) - desired, Vector3.up);
        }

        public void SetShip(string id, string cls, Color color)
        {
            shipId = id;
            shipClass = cls;
            factionColor = color;
            UpdateFactionAccent();
            if (ship) Destroy(ship.gameObject);
            ship = ProceduralShipFactory.CreateShip(shipId, shipClass, factionColor).transform;
            ship.SetParent(transform, false);
            ship.localPosition = Vector3.zero;
            ship.localRotation = Quaternion.Euler(-3f, 28f, 0f);
            FitShipToDisplay();
        }

        private void Update()
        {
            if (ship) ship.Rotate(Vector3.up, Time.deltaTime * 5f, Space.World);
        }

        private void BuildHangar()
        {
            ConfigureEnvironment();

            var voidMaterial = ProceduralShipFactory.GetMaterial("hangar-void-v2",
                new Color(0.004f, 0.008f, 0.016f), 0.2f, 0.05f);
            var floorMaterial = ProceduralShipFactory.GetMaterial("hangar-floor-v2",
                new Color(0.014f, 0.028f, 0.052f), 0.52f, 0.72f);
            var structureMaterial = ProceduralShipFactory.GetMaterial("hangar-structure-v2",
                new Color(0.035f, 0.055f, 0.085f), 0.46f, 0.82f);
            var panelMaterial = ProceduralShipFactory.GetMaterial("hangar-panel-v2",
                new Color(0.058f, 0.088f, 0.13f), 0.4f, 0.64f);
            accentMaterial = ProceduralShipFactory.GetMaterial("hangar-accent-v2",
                AccentBaseColor(factionColor), 0.32f, 0.58f, true);

            CreateBox("HangarFloor", new Vector3(0f, -0.65f, 3.5f), new Vector3(29f, 0.5f, 28f), floorMaterial);
            CreateBox("BackWall", new Vector3(0f, 5.25f, 14f), new Vector3(29f, 12f, 0.7f), structureMaterial);
            CreateBox("LeftWall", new Vector3(-14.1f, 5.1f, 3.5f), new Vector3(0.65f, 11.8f, 21f), structureMaterial);
            CreateBox("RightWall", new Vector3(14.1f, 5.1f, 3.5f), new Vector3(0.65f, 11.8f, 21f), structureMaterial);

            BuildBackWall(voidMaterial, panelMaterial);
            BuildWallPanels(panelMaterial, structureMaterial);
            BuildPerspectiveFrames(structureMaterial, panelMaterial);
            BuildLandingPad(floorMaterial, panelMaterial);
            BuildFloorGrid(voidMaterial);
            BuildFloorGuides(panelMaterial);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = new Vector3(1.2f, 6.35f, -17.2f);
            cameraObject.transform.LookAt(transform.TransformPoint(ShipDisplayCenter));
            var camera = cameraObject.AddComponent<Camera>();
            hangarCamera = camera;
            // Seed the orbit rig from the authored framing so the first frame
            // matches the old fixed camera exactly.
            var toCenter = transform.TransformPoint(ShipDisplayCenter) - cameraObject.transform.position;
            orbitDistance = toCenter.magnitude;
            orbitPitch = Mathf.Asin(-toCenter.y / Mathf.Max(0.01f, orbitDistance)) * Mathf.Rad2Deg;
            orbitYaw = Mathf.Atan2(toCenter.x, toCenter.z) * Mathf.Rad2Deg;
            orbitYawBase = orbitYaw;
            orbitPitch = Mathf.Clamp(orbitPitch, MinOrbitPitch, MaxOrbitPitch);
            camera.tag = "MainCamera";
            camera.fieldOfView = 49f;
            camera.nearClipPlane = 0.15f;
            camera.farClipPlane = 110f;
            CameraRenderQuality.Configure(camera);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.002f, 0.006f, 0.014f);

            CreateLighting();
            CreatePostProcessing();
            SetShip(shipId, shipClass, factionColor);
        }

        private void ConfigureEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.042f, 0.075f, 0.14f);
            RenderSettings.ambientEquatorColor = new Color(0.02f, 0.038f, 0.075f);
            RenderSettings.ambientGroundColor = new Color(0.008f, 0.012f, 0.024f);
            RenderSettings.ambientIntensity = 0.82f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.008f, 0.018f, 0.035f);
            RenderSettings.fogStartDistance = 22f;
            RenderSettings.fogEndDistance = 70f;
        }

        private void BuildBackWall(Material voidMaterial, Material panelMaterial)
        {
            CreateBox("ServiceBayVoid", new Vector3(-2.2f, 4.2f, 13.55f), new Vector3(11.8f, 8.2f, 0.22f), voidMaterial);
            CreateBox("ServiceBayHeader", new Vector3(-2.2f, 8.55f, 13.25f), new Vector3(12.8f, 0.55f, 0.55f), panelMaterial);
            CreateBox("ServiceBayLeft", new Vector3(-8.35f, 4.25f, 13.2f), new Vector3(0.55f, 8.7f, 0.55f), panelMaterial);
            CreateBox("ServiceBayRight", new Vector3(3.95f, 4.25f, 13.2f), new Vector3(0.55f, 8.7f, 0.55f), panelMaterial);
            CreateBox("ServiceBayLeftLight", new Vector3(-8.02f, 4.25f, 12.83f),
                new Vector3(0.1f, 7.65f, 0.12f), accentMaterial);
            CreateBox("ServiceBayRightLight", new Vector3(3.62f, 4.25f, 12.83f),
                new Vector3(0.1f, 7.65f, 0.12f), accentMaterial);
            CreateBox("ServiceBayThreshold", new Vector3(-2.2f, 0.5f, 12.84f),
                new Vector3(11.55f, 0.1f, 0.12f), accentMaterial);

            for (var i = 0; i < 5; i++)
            {
                var y = 1.25f + i * 1.42f;
                CreateBox($"ServiceBaySlat_{i}", new Vector3(-2.2f, y, 13.35f),
                    new Vector3(10.8f, 0.16f, 0.16f), panelMaterial);
            }
        }

        private void BuildWallPanels(Material panelMaterial, Material structureMaterial)
        {
            for (var i = 0; i < 5; i++)
            {
                var z = -4.8f + i * 4.1f;
                CreateBox($"LeftWallPanel_{i}", new Vector3(-13.72f, 4.25f, z),
                    new Vector3(0.08f, 6.8f, 3.35f), i % 2 == 0 ? panelMaterial : structureMaterial);
                CreateBox($"RightWallPanel_{i}", new Vector3(13.72f, 4.25f, z),
                    new Vector3(0.08f, 6.8f, 3.35f), i % 2 == 0 ? panelMaterial : structureMaterial);
                CreateBox($"LeftWallGuide_{i}", new Vector3(-13.62f, 1.35f, z),
                    new Vector3(0.1f, 0.1f, 1.9f), accentMaterial);
                CreateBox($"RightWallGuide_{i}", new Vector3(13.62f, 1.35f, z),
                    new Vector3(0.1f, 0.1f, 1.9f), accentMaterial);
            }

            for (var i = 0; i < 3; i++)
            {
                var x = -7.8f + i * 7.8f;
                CreateBox($"CeilingRail_{i}", new Vector3(x, 10.1f, 4.2f),
                    new Vector3(0.16f, 0.12f, 14.5f), panelMaterial);
            }
        }

        private void BuildPerspectiveFrames(Material structureMaterial, Material panelMaterial)
        {
            var depths = new[] { -3.5f, 2f, 7.5f, 12.5f };
            for (var i = 0; i < depths.Length; i++)
            {
                var z = depths[i];
                var inset = i * 0.32f;
                CreateBox($"Frame_{i}_Left", new Vector3(-12.1f + inset, 5.25f, z),
                    new Vector3(0.5f, 10.8f, 0.55f), structureMaterial);
                CreateBox($"Frame_{i}_Right", new Vector3(12.1f - inset, 5.25f, z),
                    new Vector3(0.5f, 10.8f, 0.55f), structureMaterial);
                CreateBox($"Frame_{i}_Top", new Vector3(0f, 10.55f, z),
                    new Vector3(24.6f - inset * 2f, 0.5f, 0.55f), structureMaterial);

                CreateBox($"Frame_{i}_LeftInset", new Vector3(-11.55f + inset, 5.25f, z - 0.03f),
                    new Vector3(0.1f, 8.2f, 0.12f), panelMaterial);
                CreateBox($"Frame_{i}_RightInset", new Vector3(11.55f - inset, 5.25f, z - 0.03f),
                    new Vector3(0.1f, 8.2f, 0.12f), panelMaterial);
            }
        }

        private void BuildLandingPad(Material floorMaterial, Material panelMaterial)
        {
            CreateCylinder("LandingPadBase", PadCenter + Vector3.up * -0.28f,
                new Vector3(6.6f, 0.24f, 6.6f), floorMaterial);
            CreateCylinder("LandingPadAccentRing", PadCenter + Vector3.up * -0.03f,
                new Vector3(6.25f, 0.12f, 6.25f), accentMaterial);
            CreateCylinder("LandingPadDeck", PadCenter + Vector3.up * 0.04f,
                new Vector3(5.85f, 0.13f, 5.85f), panelMaterial);
            CreateCylinder("LandingPadInset", PadCenter + Vector3.up * 0.13f,
                new Vector3(4.9f, 0.08f, 4.9f), floorMaterial);

            for (var i = 0; i < 16; i++)
            {
                var angle = i * Mathf.PI * 2f / 16f;
                var position = PadCenter + new Vector3(Mathf.Cos(angle) * 5.45f, 0.24f, Mathf.Sin(angle) * 5.45f);
                CreateBox($"PadMarker_{i}", position, new Vector3(0.72f, 0.045f, 0.16f), accentMaterial,
                    new Vector3(0f, -angle * Mathf.Rad2Deg, 0f));
            }
        }

        private void BuildFloorGuides(Material panelMaterial)
        {
            for (var i = 0; i < 8; i++)
            {
                var z = -7f + i * 2.65f;
                CreateBox($"LeftGuide_{i}", new Vector3(-10.4f, -0.34f, z),
                    new Vector3(0.22f, 0.055f, 1.45f), i % 3 == 0 ? accentMaterial : panelMaterial);
                CreateBox($"RightGuide_{i}", new Vector3(7.5f, -0.34f, z),
                    new Vector3(0.22f, 0.055f, 1.45f), i % 3 == 0 ? accentMaterial : panelMaterial);
            }
        }

        private void BuildFloorGrid(Material seamMaterial)
        {
            for (var i = 0; i < 7; i++)
            {
                var x = -12f + i * 4f;
                CreateBox($"FloorLongSeam_{i}", new Vector3(x, -0.385f, 3.5f),
                    new Vector3(0.055f, 0.025f, 27f), seamMaterial);
            }

            for (var i = 0; i < 8; i++)
            {
                var z = -8.2f + i * 3.35f;
                CreateBox($"FloorCrossSeam_{i}", new Vector3(0f, -0.38f, z),
                    new Vector3(28.2f, 0.025f, 0.055f), seamMaterial);
            }
        }

        private void CreateLighting()
        {
            var ambientObject = new GameObject("Hangar Ambient Light");
            ambientObject.transform.SetParent(transform, false);
            ambientObject.transform.localRotation = Quaternion.Euler(42f, -28f, 0f);
            var ambient = ambientObject.AddComponent<Light>();
            ambient.type = LightType.Directional;
            ambient.color = new Color(0.42f, 0.58f, 0.85f);
            ambient.intensity = 0.32f;

            var key = CreateSpotLight("Hangar Key Light", new Vector3(-7.5f, 10.5f, -5.5f),
                ShipDisplayCenter, new Color(0.66f, 0.82f, 1f), 42f, 42f, 54f, true);
            key.shadowStrength = 0.7f;

            rimLight = CreateSpotLight("Hangar Rim Light", new Vector3(5f, 8f, 10f),
                ShipDisplayCenter, AccentLightColor(factionColor), 34f, 38f, 48f, false);

            var fillObject = new GameObject("Hangar Fill Light");
            fillObject.transform.SetParent(transform, false);
            fillObject.transform.localPosition = new Vector3(6f, 4.5f, -2f);
            fillLight = fillObject.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.color = AccentLightColor(factionColor);
            fillLight.intensity = 9f;
            fillLight.range = 22f;
            fillLight.shadows = LightShadows.None;
        }

        private Light CreateSpotLight(string lightName, Vector3 position, Vector3 target, Color color,
            float intensity, float range, float angle, bool shadows)
        {
            var lightObject = new GameObject(lightName);
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.localPosition = position;
            lightObject.transform.LookAt(transform.TransformPoint(target));
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return light;
        }

        private VolumeProfile hangarProfile;

        private void OnDestroy()
        {
            if (hangarProfile) Destroy(hangarProfile);
        }

        private void CreatePostProcessing()
        {
            var volumeObject = new GameObject("Hangar Volume");
            volumeObject.transform.SetParent(transform, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            // Tracked so OnDestroy can destroy it: runtime profiles survive
            // scene unloads and would otherwise leak on every dock.
            var profile = hangarProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>();
            bloom.active = true;
            bloom.intensity.Override(0.32f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.58f);
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>();
            color.contrast.Override(10f);
            color.saturation.Override(-6f);
            color.colorFilter.Override(new Color(0.94f, 0.98f, 1f));
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.72f);
            volume.profile = profile;
        }

        private void FitShipToDisplay()
        {
            var renderers = ship.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                ship.localPosition = ShipDisplayCenter;
                return;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            var targetSpan = shipClass switch
            {
                "battleship" => 11.2f,
                "cruiser" => 10.4f,
                "destroyer" => 9.8f,
                _ => 9.2f
            };
            var horizontalSpan = Mathf.Max(bounds.size.x, bounds.size.z);
            var verticalSpan = Mathf.Max(bounds.size.y, 0.01f);
            var scale = Mathf.Min(targetSpan / Mathf.Max(horizontalSpan, 0.01f), 5.5f / verticalSpan);
            scale = Mathf.Clamp(scale, 0.08f, 35f);
            ship.localScale *= scale;

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            ship.position += transform.TransformPoint(ShipDisplayCenter) - bounds.center;
        }

        private void UpdateFactionAccent()
        {
            if (accentMaterial)
            {
                var color = AccentBaseColor(factionColor);
                accentMaterial.color = color;
                accentMaterial.SetColor("_BaseColor", color);
                accentMaterial.SetColor("_EmissionColor", color * 1.65f);
            }

            if (rimLight) rimLight.color = AccentLightColor(factionColor);
            if (fillLight) fillLight.color = AccentLightColor(factionColor);
        }

        private static Color AccentBaseColor(Color faction)
        {
            return Color.Lerp(new Color(0.025f, 0.2f, 0.36f), faction, 0.34f) * 0.58f;
        }

        private static Color AccentLightColor(Color faction)
        {
            return Color.Lerp(new Color(0.22f, 0.62f, 1f), faction, 0.35f);
        }

        private GameObject CreateBox(string objectName, Vector3 position, Vector3 scale, Material material,
            Vector3 rotation = default)
        {
            return CreatePrimitive(PrimitiveType.Cube, objectName, position, scale, material, rotation);
        }

        private GameObject CreateCylinder(string objectName, Vector3 position, Vector3 scale, Material material)
        {
            return CreatePrimitive(PrimitiveType.Cylinder, objectName, position, scale, material, Vector3.zero);
        }

        private GameObject CreatePrimitive(PrimitiveType primitiveType, string objectName, Vector3 position,
            Vector3 scale, Material material, Vector3 rotation)
        {
            var result = GameObject.CreatePrimitive(primitiveType);
            result.name = objectName;
            result.transform.SetParent(transform, false);
            result.transform.localPosition = position;
            result.transform.localEulerAngles = rotation;
            result.transform.localScale = scale;
            result.GetComponent<Renderer>().sharedMaterial = material;
            if (result.TryGetComponent<Collider>(out var collider)) Destroy(collider);
            return result;
        }
    }
}
