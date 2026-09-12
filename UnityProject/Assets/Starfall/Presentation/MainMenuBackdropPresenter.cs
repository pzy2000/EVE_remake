using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Starfall.Presentation
{
    [DisallowMultipleComponent]
    public sealed class MainMenuBackdropPresenter : MonoBehaviour
    {
        private Transform planet;
        private Transform heroShip;
        private Vector3 shipBasePosition;

        private void Awake()
        {
            BuildBackdrop();
        }

        private void Update()
        {
            if (planet) planet.Rotate(Vector3.up, Time.unscaledDeltaTime * 1.8f, Space.World);
            if (heroShip)
            {
                heroShip.localPosition = shipBasePosition + Vector3.up * (Mathf.Sin(Time.unscaledTime * 0.42f) * 0.18f);
                heroShip.Rotate(Vector3.up, Time.unscaledDeltaTime * 0.55f, Space.World);
            }
        }

        private void BuildBackdrop()
        {
            var backdropRoot = new GameObject("Main Menu 3D Backdrop").transform;
            backdropRoot.SetParent(transform, false);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(backdropRoot, false);
            cameraObject.transform.localPosition = new Vector3(0f, 0f, -10f);
            cameraObject.transform.localRotation = Quaternion.identity;
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.001f, 0.003f, 0.01f);
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2500f;
            CameraRenderQuality.Configure(camera);

            ProceduralSpaceMaterials.CreateSkyDome(camera.transform, "main-menu", 73421,
                new Color(0.02f, 0.28f, 0.48f), new Color(0.30f, 0.08f, 0.38f));

            var planetObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planetObject.name = "Cyan Frontier Planet";
            planetObject.transform.SetParent(backdropRoot, false);
            planetObject.transform.localPosition = new Vector3(10.8f, -4.6f, 25f);
            planetObject.transform.localScale = Vector3.one * 13.5f;
            planetObject.GetComponent<Renderer>().sharedMaterial = ProceduralSpaceMaterials.GetPlanetMaterial(
                "main-menu-frontier", new Color(0.055f, 0.32f, 0.52f), false);
            if (planetObject.TryGetComponent<Collider>(out var planetCollider)) Destroy(planetCollider);
            planet = planetObject.transform;
            CreatePlanetRing(planet);

            var shipObject = ProceduralShipFactory.CreateShip("acolyte", "frigate", new Color(1f, 0.64f, 0.18f));
            shipObject.name = "Menu Hero Ship";
            shipObject.transform.SetParent(backdropRoot, false);
            shipObject.transform.localRotation = Quaternion.Euler(8f, -42f, -5f);
            FitToSpan(shipObject.transform, 5.2f);
            shipBasePosition = new Vector3(-10.2f, -1.15f, 14.5f);
            shipObject.transform.localPosition = shipBasePosition;
            heroShip = shipObject.transform;

            var keyObject = new GameObject("Backdrop Key Light");
            keyObject.transform.SetParent(backdropRoot, false);
            keyObject.transform.localRotation = Quaternion.Euler(28f, -36f, 0f);
            var key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.62f, 0.78f, 1f);
            key.intensity = 1.3f;

            var rimObject = new GameObject("Backdrop Rim Light");
            rimObject.transform.SetParent(backdropRoot, false);
            rimObject.transform.localPosition = new Vector3(-4f, 2f, 10f);
            var rim = rimObject.AddComponent<Light>();
            rim.type = LightType.Point;
            rim.color = new Color(0.12f, 0.72f, 1f);
            rim.intensity = 10f;
            rim.range = 24f;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.03f, 0.08f, 0.17f);
            RenderSettings.ambientEquatorColor = new Color(0.012f, 0.035f, 0.075f);
            RenderSettings.ambientGroundColor = new Color(0.003f, 0.006f, 0.015f);
            RenderSettings.ambientIntensity = 0.9f;

            CreatePostProcessing(backdropRoot);
        }

        private static void CreatePlanetRing(Transform planetTransform)
        {
            var ring = new GameObject("Orbital Ring");
            ring.transform.SetParent(planetTransform, false);
            ring.transform.localRotation = Quaternion.Euler(68f, 5f, -16f);
            var line = ring.AddComponent<LineRenderer>();
            const int segments = 128;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = segments;
            line.startWidth = 0.035f;
            line.endWidth = 0.035f;
            line.sharedMaterial = ProceduralSpaceMaterials.GetGlowMaterial("main-menu-ring",
                new Color(0.13f, 0.68f, 0.95f), 1.15f);
            for (var i = 0; i < segments; i++)
            {
                var angle = i * Mathf.PI * 2f / segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.72f, 0f, Mathf.Sin(angle) * 0.72f));
            }
        }

        private static void FitToSpan(Transform target, float targetSpan)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            var span = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (span > 0.001f) target.localScale *= targetSpan / span;
        }

        private static readonly System.Collections.Generic.List<VolumeProfile> createdProfiles =
            new System.Collections.Generic.List<VolumeProfile>();

        private void OnDestroy()
        {
            // Runtime profiles survive scene unloads; the menu is the only
            // creator here, so it owns disposing every profile it made.
            for (var i = 0; i < createdProfiles.Count; i++)
                if (createdProfiles[i]) Destroy(createdProfiles[i]);
            createdProfiles.Clear();
        }

        private static void CreatePostProcessing(Transform parent)
        {
            var volumeObject = new GameObject("Main Menu Volume");
            volumeObject.transform.SetParent(parent, false);
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            createdProfiles.Add(profile);
            var bloom = profile.Add<Bloom>();
            bloom.active = true;
            bloom.intensity.Override(0.42f);
            bloom.threshold.Override(0.85f);
            bloom.scatter.Override(0.66f);
            var tone = profile.Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>();
            color.contrast.Override(8f);
            color.saturation.Override(-4f);
            var vignette = profile.Add<Vignette>();
            vignette.intensity.Override(0.24f);
            vignette.smoothness.Override(0.75f);
            volume.profile = profile;
        }
    }
}
