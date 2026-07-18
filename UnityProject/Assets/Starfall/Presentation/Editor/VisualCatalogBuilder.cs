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
    [InitializeOnLoad]
    public static class VisualCatalogBuilder
    {
        private const int GenerationVersion = 1;
        private const string GeneratedRoot = "Assets/Starfall/Art/Generated";
        private const string MaterialRoot = GeneratedRoot + "/Materials";
        private const string ShipPrefabRoot = GeneratedRoot + "/Prefabs/Ships";
        private const string WorldPrefabRoot = GeneratedRoot + "/Prefabs/World";
        private const string CatalogPath = "Assets/Starfall/Presentation/Resources/VisualCatalog.asset";
        private const string QuaterniusModelRoot = "Assets/Starfall/Art/ThirdParty/QuaterniusUltimateSpaceships/Models";
        private const string QuaterniusTextureRoot = "Assets/Starfall/Art/ThirdParty/QuaterniusUltimateSpaceships/Textures";
        private const string MajadroidModel = "Assets/Starfall/Art/ThirdParty/MajadroidLowPoly/Models/LowPoly Spaceships ReadyToFly.fbx";
        private const string MajadroidTextureRoot = "Assets/Starfall/Art/ThirdParty/MajadroidLowPoly/Textures";
        private const string KenneyModelRoot = "Assets/Starfall/Art/ThirdParty/KenneyModularSpace/Models";
        private const string KenneyTexture = "Assets/Starfall/Art/ThirdParty/KenneyModularSpace/Textures/colormap.png";

        private static readonly Color Aurelian = new(1f, 0.64f, 0.16f);
        private static readonly Color Kaldari = new(0.18f, 0.52f, 1f);
        private static readonly Color Meridian = new(0.08f, 0.85f, 0.61f);
        private static readonly Color Varkhald = new(0.9f, 0.16f, 0.08f);
        private static readonly Color Sisters = new(0.95f, 0.9f, 0.74f);
        private static readonly Color Directorate = new(0.94f, 0.95f, 1f);

        private static readonly ShipSpec[] ShipSpecs =
        {
            Quaternius("acolyte", "aurelian", "frigate", "Spitfire", "Orange", Aurelian, 4f),
            Quaternius("templar", "aurelian", "destroyer", "Challenger", "Orange", Aurelian, 6f),
            Quaternius("dawnbringer", "aurelian", "cruiser", "Imperial", "Orange", Aurelian, 9f),
            Quaternius("seraph", "aurelian", "battleship", "Executioner", "Orange", Aurelian, 14f),

            Quaternius("shrike", "kaldari", "frigate", "Striker", "Blue", Kaldari, 4f),
            Quaternius("heron", "kaldari", "destroyer", "Dispatcher", "Blue", Kaldari, 6f),
            Quaternius("rook", "kaldari", "cruiser", "Omen", "Blue", Kaldari, 9f),
            Quaternius("onyx", "kaldari", "battleship", "Zenith", "Blue", Kaldari, 14f),

            Quaternius("wasp", "meridian", "frigate", "Bob", "Green", Meridian, 4f),
            Quaternius("anvil", "meridian", "destroyer", "Pancake", "Green", Meridian, 6f),
            Quaternius("mantis", "meridian", "cruiser", "Insurgent", "Green", Meridian, 9f),
            Quaternius("colossus", "meridian", "battleship", "Imperial", "Green", Meridian, 14f),

            Quaternius("fang", "varkhald", "frigate", "Challenger", "Red", Varkhald, 4f),
            Quaternius("maul", "varkhald", "destroyer", "Striker", "Red", Varkhald, 6f),
            Quaternius("broadsword", "varkhald", "cruiser", "Omen", "Red", Varkhald, 9f),
            Quaternius("stormcaller", "varkhald", "battleship", "Executioner", "Red", Varkhald, 14f),

            Majadroid("pilgrim", "sisters", "frigate", "M2_Ship_3", "tex02-512.png", Sisters, 4f),
            Majadroid("enforcer", "directorate", "cruiser", "M4_Ship_3", "tex04-512.png", Directorate, 9f),
        };

        private static bool queued;

        static VisualCatalogBuilder()
        {
            if (!Application.isBatchMode) QueueEnsureBuilt();
        }

        [MenuItem("Tools/STARFALL ODYSSEY/Rebuild Visual Catalog")]
        public static void RebuildFromMenu()
        {
            BuildAll();
        }

        private static void QueueEnsureBuilt()
        {
            if (Application.isBatchMode) return;
            if (queued) return;
            queued = true;
            EditorApplication.delayCall += EnsureBuilt;
        }

        private static void EnsureBuilt()
        {
            queued = false;
            if (Application.isBatchMode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueEnsureBuilt();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var catalog = AssetDatabase.LoadAssetAtPath<VisualCatalogAsset>(CatalogPath);
            if (catalog && catalog.GenerationVersion == GenerationVersion && ValidateCatalog(catalog, false)) return;
            BuildAll();
        }

        public static void ValidateGeneratedAssetsForCi()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<VisualCatalogAsset>(CatalogPath);
            if (!catalog) throw new InvalidOperationException("VisualCatalog.asset is missing.");
            if (catalog.GenerationVersion != GenerationVersion)
                throw new InvalidOperationException($"Visual catalog generation version is {catalog.GenerationVersion}; expected {GenerationVersion}.");
            if (!ValidateCatalog(catalog, false))
                throw new InvalidOperationException("Generated visual catalog assets are stale or incomplete.");
        }

        private static void BuildAll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueEnsureBuilt();
                return;
            }

            EnsureFolder(MaterialRoot);
            EnsureFolder(ShipPrefabRoot);
            EnsureFolder(WorldPrefabRoot);
            EnsureFolder(Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));

            var previewScene = EditorSceneManager.NewPreviewScene();
            var generatedShips = new List<ShipVisualEntry>(ShipSpecs.Length);
            GameObject stationPrefab;
            GameObject gatePrefab;
            try
            {
                foreach (var spec in ShipSpecs)
                {
                    var prefab = BuildShipPrefab(spec, previewScene);
                    generatedShips.Add(new ShipVisualEntry(spec.Id, spec.FactionId, spec.ShipClass,
                        spec.SourceLabel, prefab,
                        AssetDatabase.LoadAssetAtPath<Sprite>(ShipThumbnailGenerator.GetThumbnailAssetPath(spec.Id))));
                }
                stationPrefab = BuildStationPrefab(previewScene);
                gatePrefab = BuildGatePrefab(previewScene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<VisualCatalogAsset>(CatalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<VisualCatalogAsset>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.ConfigureGenerated(GenerationVersion, generatedShips.ToArray(), stationPrefab, gatePrefab);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!ValidateCatalog(catalog, true))
                throw new InvalidOperationException("Generated visual catalog failed validation.");

            Debug.Log($"[Starfall Visuals] Generated {generatedShips.Count} ship prefabs plus Kenney station/gate " +
                      $"and saved {CatalogPath} (generation {GenerationVersion}).");
        }

        private static GameObject BuildShipPrefab(ShipSpec spec, Scene previewScene)
        {
            var root = NewPreviewRoot($"Ship_{spec.Id}", previewScene);
            var geometry = new GameObject("Geometry");
            SceneManager.MoveGameObjectToScene(geometry, previewScene);
            geometry.transform.SetParent(root.transform, false);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.ModelPath);
            if (!source) throw new FileNotFoundException($"Missing ship model for {spec.Id}", spec.ModelPath);

            var imported = PrefabUtility.InstantiatePrefab(source, previewScene) as GameObject;
            if (!imported) throw new InvalidOperationException($"Could not instantiate {spec.ModelPath}");

            Transform visual;
            if (string.IsNullOrEmpty(spec.SourceNode))
            {
                visual = imported.transform;
            }
            else
            {
                PrefabUtility.UnpackPrefabInstance(imported, PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                visual = FindDeep(imported.transform, spec.SourceNode);
                if (!visual)
                    throw new InvalidOperationException($"Model {spec.ModelPath} has no child named {spec.SourceNode}.");
                visual.SetParent(null, true);
                UnityEngine.Object.DestroyImmediate(imported);
            }

            visual.name = spec.SourceLabel;
            visual.SetParent(geometry.transform, false);
            visual.localPosition = Vector3.zero;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.TexturePath);
            if (!texture) throw new FileNotFoundException($"Missing ship texture for {spec.Id}", spec.TexturePath);
            var hullMaterial = GetOrCreateMaterial($"M_Ship_{spec.Id}", texture, spec.MaterialTint,
                0.5f, 0.66f, false);
            ApplyMaterial(geometry, hullMaterial);

            NormalizeGeometry(geometry.transform, root.transform, spec.TargetLength);
            var bounds = CalculateRendererBounds(root);
            var weapon = AddAnchor(root.transform, "WeaponHardpoint",
                root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + 0.15f)));
            var engine = AddAnchor(root.transform, "EngineHardpoint",
                root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.min.z - 0.15f)));

            var engineMaterial = GetOrCreateMaterial($"M_Ship_{spec.Id}_Engine", null, spec.AccentColor,
                0f, 0.2f, true);
            AddEngineGlow(engine, engineMaterial, spec.TargetLength, previewScene);

            bounds = CalculateRendererBounds(root);
            AddBoxCollider(root, bounds, 1.08f);
            root.AddComponent<ShipVisualIdentity>().Configure(spec.Id, spec.FactionId, spec.ShipClass,
                spec.SourceLabel, weapon, engine);

            var path = $"{ShipPrefabRoot}/{spec.Id}.prefab";
            return SavePrefabAndReload(root, path);
        }

        private static GameObject BuildStationPrefab(Scene previewScene)
        {
            var root = NewPreviewRoot("StationModel", previewScene);
            var geometry = NewChild(root.transform, "Geometry", previewScene);
            var material = GetOrCreateMaterial("M_KenneyStation", AssetDatabase.LoadAssetAtPath<Texture2D>(KenneyTexture),
                Color.white, 0.52f, 0.62f, false);
            AddKenneyPart(geometry.transform, "room-large.fbx", Vector3.zero, Vector3.zero, material, previewScene);
            AddKenneyPart(geometry.transform, "room-wide.fbx", new Vector3(5f, 0f, 0f), new Vector3(0f, 90f, 0f), material, previewScene);
            AddKenneyPart(geometry.transform, "room-wide.fbx", new Vector3(-5f, 0f, 0f), new Vector3(0f, -90f, 0f), material, previewScene);
            AddKenneyPart(geometry.transform, "room-corner.fbx", new Vector3(0f, 0f, 5f), Vector3.zero, material, previewScene);
            AddKenneyPart(geometry.transform, "corridor.fbx", new Vector3(0f, 0f, -6f), Vector3.zero, material, previewScene);
            AddKenneyPart(geometry.transform, "corridor-junction.fbx", new Vector3(0f, 0f, -10f), Vector3.zero, material, previewScene);
            AddKenneyPart(geometry.transform, "cables.fbx", new Vector3(0f, 2f, -2f), Vector3.zero, material, previewScene);
            NormalizeGeometry(geometry.transform, root.transform, 24f);

            var accentMaterial = GetOrCreateMaterial("M_StationFactionAccent", null, new Color(0.15f, 0.75f, 1f),
                0f, 0.25f, true);
            var accent = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(accent, previewScene);
            accent.name = "FactionAccent_Station";
            accent.transform.SetParent(root.transform, false);
            var stationBounds = CalculateRendererBounds(root);
            accent.transform.localPosition = new Vector3(0f, stationBounds.extents.y * 0.55f, -stationBounds.extents.z * 0.72f);
            accent.transform.localScale = new Vector3(stationBounds.size.x * 0.42f, 0.12f, 0.18f);
            accent.GetComponent<Renderer>().sharedMaterial = accentMaterial;
            UnityEngine.Object.DestroyImmediate(accent.GetComponent<Collider>());

            stationBounds = CalculateRendererBounds(root);
            AddBoxCollider(root, stationBounds, 1.04f);
            return SavePrefabAndReload(root, $"{WorldPrefabRoot}/StationModel.prefab");
        }

        private static GameObject BuildGatePrefab(Scene previewScene)
        {
            var root = NewPreviewRoot("StargateModel", previewScene);
            var geometry = NewChild(root.transform, "Geometry", previewScene);
            var metal = GetOrCreateMaterial("M_KenneyGate", AssetDatabase.LoadAssetAtPath<Texture2D>(KenneyTexture),
                Color.white, 0.64f, 0.72f, false);
            var accent = GetOrCreateMaterial("M_GateFactionAccent", null, new Color(0.18f, 0.75f, 1f),
                0f, 0.2f, true);
            AddKenneyPart(geometry.transform, "gate.fbx", Vector3.zero, Vector3.zero, metal, previewScene);
            var laser = AddKenneyPart(geometry.transform, "gate-lasers.fbx", Vector3.zero, Vector3.zero, accent, previewScene);
            laser.name = "FactionAccent_Gate";
            NormalizeGeometry(geometry.transform, root.transform, 16f);

            var bounds = CalculateRendererBounds(root);
            var collider = root.AddComponent<SphereCollider>();
            collider.center = root.transform.InverseTransformPoint(bounds.center);
            collider.radius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z) * 1.08f;
            return SavePrefabAndReload(root, $"{WorldPrefabRoot}/StargateModel.prefab");
        }

        private static GameObject SavePrefabAndReload(GameObject root, string path)
        {
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            if (!saved) throw new InvalidOperationException($"Failed to save {path}");

            // A prefab saved from a preview scene can be represented by the transient
            // scene object until that object is destroyed. Reacquire the durable asset
            // before persisting its reference into VisualCatalog.asset.
            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!reloaded) throw new InvalidOperationException($"Failed to reload {path}");
            return reloaded;
        }

        private static GameObject AddKenneyPart(Transform parent, string fileName, Vector3 position,
            Vector3 rotation, Material material, Scene previewScene)
        {
            var path = $"{KenneyModelRoot}/{fileName}";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) throw new FileNotFoundException("Missing Kenney model", path);
            var instance = PrefabUtility.InstantiatePrefab(asset, previewScene) as GameObject;
            if (!instance) throw new InvalidOperationException($"Could not instantiate {path}");
            instance.name = Path.GetFileNameWithoutExtension(fileName);
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = position;
            instance.transform.localEulerAngles = rotation;
            ApplyMaterial(instance, material);
            return instance;
        }

        private static void NormalizeGeometry(Transform geometry, Transform root, float targetSize)
        {
            var bounds = CalculateRendererBounds(root.gameObject);
            var sourceSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (sourceSize <= 0.0001f) throw new InvalidOperationException($"{root.name} has no renderable geometry.");
            geometry.localScale *= targetSize / sourceSize;
            bounds = CalculateRendererBounds(root.gameObject);
            geometry.position -= bounds.center;
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void AddBoxCollider(GameObject root, Bounds worldBounds, float padding)
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.center = root.transform.InverseTransformPoint(worldBounds.center);
            collider.size = Vector3.Scale(worldBounds.size * padding, InverseAbs(root.transform.lossyScale));
        }

        private static Vector3 InverseAbs(Vector3 value)
        {
            return new Vector3(1f / Mathf.Max(Mathf.Abs(value.x), 0.0001f),
                1f / Mathf.Max(Mathf.Abs(value.y), 0.0001f),
                1f / Mathf.Max(Mathf.Abs(value.z), 0.0001f));
        }

        private static Transform AddAnchor(Transform parent, string name, Vector3 localPosition)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = localPosition;
            anchor.localRotation = Quaternion.identity;
            return anchor;
        }

        private static void AddEngineGlow(Transform engine, Material material, float targetLength, Scene previewScene)
        {
            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            SceneManager.MoveGameObjectToScene(glow, previewScene);
            glow.name = "FactionAccent_EngineGlow";
            glow.transform.SetParent(engine, false);
            var size = Mathf.Max(0.12f, targetLength * 0.035f);
            glow.transform.localScale = new Vector3(size, size, size * 0.45f);
            glow.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(glow.GetComponent<Collider>());
        }

        private static void ApplyMaterial(GameObject root, Material material)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                if (slots == null || slots.Length == 0)
                {
                    renderer.sharedMaterial = material;
                    continue;
                }
                for (var i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
            }
        }

        private static Material GetOrCreateMaterial(string name, Texture texture, Color color,
            float metallic, float smoothness, bool emission)
        {
            var path = $"{MaterialRoot}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("No compatible Lit shader is available.");
            if (!material)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.color = color;
            material.mainTexture = texture;
            material.enableInstancing = true;
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 2.5f);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (string.Equals(root.name, name, StringComparison.Ordinal)) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), name);
                if (found) return found;
            }
            return null;
        }

        private static GameObject NewPreviewRoot(string name, Scene previewScene)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, previewScene);
            return root;
        }

        private static GameObject NewChild(Transform parent, string name, Scene previewScene)
        {
            var child = new GameObject(name);
            SceneManager.MoveGameObjectToScene(child, previewScene);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static bool ValidateCatalog(VisualCatalogAsset catalog, bool logErrors)
        {
            var valid = catalog && catalog.Ships.Count == 18 && catalog.StationPrefab && catalog.GatePrefab;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (catalog)
            {
                foreach (var entry in catalog.Ships)
                {
                    var identity = entry?.Prefab ? entry.Prefab.GetComponent<ShipVisualIdentity>() : null;
                    var entryValid = entry != null && !string.IsNullOrWhiteSpace(entry.StableId) &&
                                     seen.Add(entry.StableId) && entry.Prefab &&
                                     entry.Prefab.GetComponent<Collider>() && identity &&
                                     identity.WeaponHardpoint && identity.EngineHardpoint;
                    valid &= entryValid;
                    if (!entryValid && logErrors)
                        Debug.LogError($"[Starfall Visuals] Invalid visual entry: {entry?.StableId ?? "<null>"}");
                }
            }
            if (!valid && logErrors) Debug.LogError("[Starfall Visuals] Visual catalog validation failed.");
            return valid;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static ShipSpec Quaternius(string id, string faction, string shipClass, string model,
            string textureColor, Color accent, float targetLength)
        {
            return new ShipSpec(id, faction, shipClass, $"{QuaterniusModelRoot}/{model}.fbx", string.Empty,
                $"{QuaterniusTextureRoot}/{model}_{textureColor}.png", $"Quaternius/{model}",
                Color.white, accent, targetLength);
        }

        private static ShipSpec Majadroid(string id, string faction, string shipClass, string sourceNode,
            string textureFile, Color tint, float targetLength)
        {
            return new ShipSpec(id, faction, shipClass, MajadroidModel, sourceNode,
                $"{MajadroidTextureRoot}/{textureFile}", $"Majadroid/{sourceNode}",
                tint, tint, targetLength);
        }

        private sealed class ShipSpec
        {
            public readonly string Id;
            public readonly string FactionId;
            public readonly string ShipClass;
            public readonly string ModelPath;
            public readonly string SourceNode;
            public readonly string TexturePath;
            public readonly string SourceLabel;
            public readonly Color MaterialTint;
            public readonly Color AccentColor;
            public readonly float TargetLength;

            public ShipSpec(string id, string factionId, string shipClass, string modelPath,
                string sourceNode, string texturePath, string sourceLabel, Color materialTint,
                Color accentColor, float targetLength)
            {
                Id = id;
                FactionId = factionId;
                ShipClass = shipClass;
                ModelPath = modelPath;
                SourceNode = sourceNode;
                TexturePath = texturePath;
                SourceLabel = sourceLabel;
                MaterialTint = materialTint;
                AccentColor = accentColor;
                TargetLength = targetLength;
            }
        }
    }
}
