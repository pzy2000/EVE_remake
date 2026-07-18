using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starfall.Presentation.Editor
{
    /// <summary>
    /// Renders the generated ship prefabs in an isolated preview scene and writes
    /// deterministic, transparent sprites back to the visual catalog.
    /// </summary>
    public static class ShipThumbnailGenerator
    {
        public const int ThumbnailSize = 256;
        public const string ThumbnailRoot = "Assets/Starfall/Art/Generated/Thumbnails";

        private const string CatalogPath = "Assets/Starfall/Presentation/Resources/VisualCatalog.asset";
        private const float FramingPadding = 1.16f;

        [MenuItem("Tools/STARFALL ODYSSEY/Generate Ship Thumbnails")]
        public static void GenerateFromMenu()
        {
            GenerateAllShipThumbnails();
        }

        /// <summary>
        /// Public entry point intended for menu use and Unity MCP execute_code.
        /// Returns the number of generated and linked ship thumbnails.
        /// </summary>
        public static int GenerateAllShipThumbnails()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Ship thumbnails can only be generated in Edit mode.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Wait for Unity compilation and asset import to finish first.");

            var catalog = AssetDatabase.LoadAssetAtPath<VisualCatalogAsset>(CatalogPath);
            if (!catalog) throw new FileNotFoundException("Visual catalog is missing.", CatalogPath);
            if (catalog.Ships == null || catalog.Ships.Count != 18)
                throw new InvalidOperationException($"Expected 18 ship visuals, found {catalog.Ships?.Count ?? 0}.");

            EnsureFolder(ThumbnailRoot);
            var outputs = new List<ThumbnailOutput>(catalog.Ships.Count);
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in catalog.Ships)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.StableId))
                    throw new InvalidOperationException("Visual catalog contains a ship without a stable ID.");
                if (!seenIds.Add(entry.StableId))
                    throw new InvalidOperationException($"Visual catalog contains duplicate ship ID '{entry.StableId}'.");
                if (!entry.Prefab)
                    throw new InvalidOperationException($"Ship '{entry.StableId}' has no prefab to render.");

                var assetPath = GetThumbnailAssetPath(entry.StableId);
                var png = RenderPrefabToPng(entry.Prefab);
                WriteIfChanged(assetPath, png);
                outputs.Add(new ThumbnailOutput(entry, assetPath));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            foreach (var output in outputs)
            {
                ConfigureSpriteImporter(output.AssetPath);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(output.AssetPath);
                if (!sprite)
                    throw new InvalidOperationException($"Could not import thumbnail as Sprite: {output.AssetPath}");
                output.Entry.SetThumbnail(sprite);
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            var missing = catalog.Ships.Where(entry => entry == null || !entry.Thumbnail).ToArray();
            if (missing.Length != 0)
                throw new InvalidOperationException($"Thumbnail generation left {missing.Length} catalog entries unresolved.");

            Debug.Log($"[Starfall Visuals] Generated and linked {outputs.Count} transparent " +
                      $"{ThumbnailSize}x{ThumbnailSize} ship thumbnails in {ThumbnailRoot}.");
            return outputs.Count;
        }

        public static string GetThumbnailAssetPath(string stableId)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException("A stable ship ID is required.", nameof(stableId));

            var invalid = Path.GetInvalidFileNameChars();
            var safeId = new string(stableId.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
            return $"{ThumbnailRoot}/{safeId}.png";
        }

        private static byte[] RenderPrefabToPng(GameObject prefab)
        {
            Scene previewScene = default;
            RenderTexture target = null;
            RenderTexture resolved = null;
            Texture2D pixels = null;
            Camera thumbnailCamera = null;
            var previousActive = RenderTexture.active;

            try
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                var instance = PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
                if (!instance) throw new InvalidOperationException($"Could not instantiate ship prefab '{prefab.name}'.");
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                var bounds = CalculateRendererBounds(instance);
                thumbnailCamera = CreateCamera(previewScene, bounds);
                CreateLighting(previewScene, bounds.center);

                target = RenderTexture.GetTemporary(ThumbnailSize, ThumbnailSize, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
                target.name = $"StarfallThumbnail_{prefab.name}_MSAA";
                target.filterMode = FilterMode.Bilinear;
                target.wrapMode = TextureWrapMode.Clamp;

                resolved = RenderTexture.GetTemporary(ThumbnailSize, ThumbnailSize, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 1);
                resolved.name = $"StarfallThumbnail_{prefab.name}_Resolved";
                resolved.filterMode = FilterMode.Bilinear;
                resolved.wrapMode = TextureWrapMode.Clamp;

                thumbnailCamera.targetTexture = target;
                thumbnailCamera.Render();
                thumbnailCamera.targetTexture = null;
                Graphics.Blit(target, resolved);

                RenderTexture.active = resolved;
                pixels = new Texture2D(ThumbnailSize, ThumbnailSize, TextureFormat.RGBA32, false, false)
                {
                    name = $"StarfallThumbnail_{prefab.name}_Pixels"
                };
                pixels.ReadPixels(new Rect(0f, 0f, ThumbnailSize, ThumbnailSize), 0, 0, false);
                pixels.Apply(false, false);
                return pixels.EncodeToPNG();
            }
            finally
            {
                if (thumbnailCamera) thumbnailCamera.targetTexture = null;
                RenderTexture.active = previousActive;
                if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
                if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
                if (resolved) RenderTexture.ReleaseTemporary(resolved);
                if (target) RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Camera CreateCamera(Scene previewScene, Bounds bounds)
        {
            var cameraObject = new GameObject("ThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.scene = previewScene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.orthographic = true;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
            camera.nearClipPlane = 0.01f;

            var radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            var viewDirection = new Vector3(0.8f, 0.48f, -1.25f).normalized;
            var distance = radius * 4f + 1f;
            camera.transform.position = bounds.center + viewDirection * distance;
            camera.transform.LookAt(bounds.center, Vector3.up);
            camera.farClipPlane = distance + radius * 4f;
            camera.orthographicSize = CalculateOrthographicSize(camera.transform, bounds) * FramingPadding;
            return camera;
        }

        private static void CreateLighting(Scene previewScene, Vector3 target)
        {
            CreateDirectionalLight(previewScene, "ThumbnailKeyLight", target,
                Quaternion.Euler(34f, -38f, 0f), new Color(1f, 0.91f, 0.78f), 1.35f);
            CreateDirectionalLight(previewScene, "ThumbnailFillLight", target,
                Quaternion.Euler(325f, 142f, 18f), new Color(0.42f, 0.68f, 1f), 0.72f);
        }

        private static void CreateDirectionalLight(Scene previewScene, string name, Vector3 position,
            Quaternion rotation, Color color, float intensity)
        {
            var lightObject = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(lightObject, previewScene);
            lightObject.transform.SetPositionAndRotation(position, rotation);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.Soft;
        }

        private static float CalculateOrthographicSize(Transform cameraTransform, Bounds bounds)
        {
            var extents = bounds.extents;
            var maxX = 0f;
            var maxY = 0f;
            for (var x = -1; x <= 1; x += 2)
            for (var y = -1; y <= 1; y += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var corner = bounds.center + Vector3.Scale(extents, new Vector3(x, y, z));
                var local = cameraTransform.InverseTransformPoint(corner);
                maxX = Mathf.Max(maxX, Mathf.Abs(local.x));
                maxY = Mathf.Max(maxY, Mathf.Abs(local.y));
            }
            return Mathf.Max(maxX, maxY, 0.1f);
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException($"Ship prefab '{root.name}' contains no renderers.");

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static void ConfigureSpriteImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (!importer) throw new InvalidOperationException($"No TextureImporter exists for {assetPath}");

            var changed = importer.textureType != TextureImporterType.Sprite ||
                          importer.spriteImportMode != SpriteImportMode.Single ||
                          importer.alphaIsTransparency != true ||
                          importer.mipmapEnabled != false ||
                          importer.sRGBTexture != true ||
                          importer.wrapMode != TextureWrapMode.Clamp ||
                          importer.filterMode != FilterMode.Bilinear ||
                          importer.npotScale != TextureImporterNPOTScale.None ||
                          importer.textureCompression != TextureImporterCompression.Uncompressed ||
                          importer.maxTextureSize != ThumbnailSize ||
                          !Mathf.Approximately(importer.spritePixelsPerUnit, 100f);
            if (!changed) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = ThumbnailSize;
            importer.spritePixelsPerUnit = 100f;
            importer.SaveAndReimport();
        }

        private static void WriteIfChanged(string assetPath, byte[] bytes)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
                throw new InvalidOperationException("Could not resolve the Unity project root.");

            var absolutePath = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(absolutePath) && File.ReadAllBytes(absolutePath).SequenceEqual(bytes)) return;
            File.WriteAllBytes(absolutePath, bytes);
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = $"{current}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        private sealed class ThumbnailOutput
        {
            public readonly ShipVisualEntry Entry;
            public readonly string AssetPath;

            public ThumbnailOutput(ShipVisualEntry entry, string assetPath)
            {
                Entry = entry;
                AssetPath = assetPath;
            }
        }
    }
}
