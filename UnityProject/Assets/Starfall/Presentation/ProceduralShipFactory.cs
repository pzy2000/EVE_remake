using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.Presentation
{
    public static class ProceduralShipFactory
    {
        private static readonly Dictionary<string, Material> Materials = new();
        private static VisualCatalogAsset visualCatalog;
        private static bool catalogLoadAttempted;

        private static VisualCatalogAsset VisualCatalog
        {
            get
            {
                if (!catalogLoadAttempted)
                {
                    visualCatalog = Resources.Load<VisualCatalogAsset>("VisualCatalog");
                    catalogLoadAttempted = true;
                }
                return visualCatalog;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            visualCatalog = null;
            catalogLoadAttempted = false;
            Materials.Clear();
        }

        public static GameObject CreateShip(string shipId, string shipClass, Color factionColor, bool hostile = false)
        {
            if (TryCreateCatalogShip(shipId, factionColor, hostile, out var catalogShip)) return catalogShip;

            var root = new GameObject($"Ship_{shipId}");
            var hash = StableHash(shipId);
            var classScale = shipClass switch
            {
                "destroyer" => 1.45f,
                "cruiser" => 2.15f,
                "battleship" => 3.25f,
                _ => 1f
            };
            var hullColor = hostile ? Color.Lerp(factionColor, new Color(0.55f, 0.04f, 0.05f), 0.45f) : factionColor;
            var hull = GetMaterial($"hull-{ColorUtility.ToHtmlStringRGB(hullColor)}", hullColor, 0.72f, 0.42f);
            var trim = GetMaterial("trim-dark", new Color(0.025f, 0.04f, 0.075f), 0.88f, 0.35f);
            var glow = GetMaterial($"glow-{ColorUtility.ToHtmlStringRGB(hullColor)}", hullColor * 1.35f, 0.2f, 0.75f, true);

            CreatePart(root.transform, PrimitiveType.Capsule, "Spine", new Vector3(0, 0, 0), new Vector3(0.42f, 0.72f, 1.4f), new Vector3(90, 0, 0), hull);
            CreatePart(root.transform, PrimitiveType.Cube, "Dorsal", new Vector3(0, 0.25f, -0.05f), new Vector3(0.42f, 0.18f, 1.55f), new Vector3(0, 0, 0), trim);

            var wingStyle = Math.Abs(hash % 4);
            var wingSweep = 12f + wingStyle * 9f;
            var wingWidth = 0.9f + wingStyle * 0.17f;
            for (var side = -1; side <= 1; side += 2)
            {
                CreatePart(root.transform, PrimitiveType.Cube, side < 0 ? "Wing_L" : "Wing_R",
                    new Vector3(side * 0.7f, -0.02f, -0.05f),
                    new Vector3(wingWidth, 0.08f, 0.9f),
                    new Vector3(0, side * wingSweep, side * -4f), hull);
                CreatePart(root.transform, PrimitiveType.Cylinder, side < 0 ? "Engine_L" : "Engine_R",
                    new Vector3(side * 0.52f, -0.04f, -0.9f),
                    new Vector3(0.18f, 0.42f, 0.18f),
                    new Vector3(90, 0, 0), trim);
                CreatePart(root.transform, PrimitiveType.Sphere, side < 0 ? "EngineGlow_L" : "EngineGlow_R",
                    new Vector3(side * 0.52f, -0.04f, -1.34f),
                    new Vector3(0.16f, 0.16f, 0.07f), Vector3.zero, glow, false);
            }

            var noseLength = 0.55f + Math.Abs((hash >> 3) % 4) * 0.13f;
            CreatePart(root.transform, PrimitiveType.Cylinder, "Nose", new Vector3(0, 0, 1.1f),
                new Vector3(0.22f, noseLength, 0.22f), new Vector3(90, 0, 0), hull);

            var antennaCount = 1 + Math.Abs((hash >> 7) % 3);
            for (var i = 0; i < antennaCount; i++)
            {
                var x = (i - (antennaCount - 1) * 0.5f) * 0.28f;
                CreatePart(root.transform, PrimitiveType.Cylinder, $"Hardpoint_{i}", new Vector3(x, 0.34f, 0.38f),
                    new Vector3(0.045f, 0.22f, 0.045f), new Vector3(90, 0, 0), trim);
            }

            root.transform.localScale = Vector3.one * (2.4f * classScale);
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(2.2f, 0.85f, 3.15f);
            collider.center = new Vector3(0, 0, -0.05f);
            return root;
        }

        public static GameObject CreateStation(Color factionColor)
        {
            if (VisualCatalog && VisualCatalog.StationPrefab)
            {
                var station = UnityEngine.Object.Instantiate(VisualCatalog.StationPrefab);
                station.name = "StationModel";
                ApplyAccentTint(station, factionColor);
                return station;
            }

            var root = new GameObject("StationModel");
            var hull = GetMaterial("station-hull", new Color(0.08f, 0.1f, 0.15f), 0.82f, 0.55f);
            var trim = GetMaterial($"station-{ColorUtility.ToHtmlStringRGB(factionColor)}", factionColor, 0.65f, 0.62f);
            CreatePart(root.transform, PrimitiveType.Cylinder, "Core", Vector3.zero, new Vector3(4.5f, 2.2f, 4.5f), Vector3.zero, hull);
            CreatePart(root.transform, PrimitiveType.Cylinder, "RingA", Vector3.zero, new Vector3(7.5f, 0.28f, 7.5f), Vector3.zero, trim);
            CreatePart(root.transform, PrimitiveType.Cylinder, "RingB", new Vector3(0, 2.2f, 0), new Vector3(5.7f, 0.2f, 5.7f), Vector3.zero, trim);
            for (var i = 0; i < 6; i++)
            {
                var angle = i * Mathf.PI * 2f / 6f;
                var p = new Vector3(Mathf.Cos(angle) * 6.2f, 0, Mathf.Sin(angle) * 6.2f);
                CreatePart(root.transform, PrimitiveType.Cube, $"DockArm_{i}", p, new Vector3(1.25f, 0.55f, 4.6f), new Vector3(0, -i * 60f, 0), hull);
            }
            root.AddComponent<SphereCollider>().radius = 8f;
            return root;
        }

        public static GameObject CreateGate(Color color)
        {
            if (VisualCatalog && VisualCatalog.GatePrefab)
            {
                var gate = UnityEngine.Object.Instantiate(VisualCatalog.GatePrefab);
                gate.name = "StargateModel";
                ApplyAccentTint(gate, color);
                return gate;
            }

            var root = new GameObject("StargateModel");
            var metal = GetMaterial("gate-metal", new Color(0.07f, 0.09f, 0.14f), 0.9f, 0.65f);
            var glow = GetMaterial($"gate-{ColorUtility.ToHtmlStringRGB(color)}", color * 1.5f, 0.3f, 0.7f, true);
            const int segments = 16;
            const float radius = 5.2f;
            for (var i = 0; i < segments; i++)
            {
                var a = i * Mathf.PI * 2f / segments;
                var p = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0);
                var part = CreatePart(root.transform, PrimitiveType.Cube, $"Ring_{i}", p,
                    new Vector3(1.05f, 0.42f, 0.5f), new Vector3(0, 0, a * Mathf.Rad2Deg + 90f), i % 2 == 0 ? glow : metal);
                part.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg + 90f);
            }
            root.AddComponent<SphereCollider>().radius = 6.2f;
            return root;
        }

        public static Material GetMaterial(string key, Color color, float smoothness = 0.5f, float metallic = 0.2f, bool emission = false)
        {
            if (Materials.TryGetValue(key, out var cached) && cached) return cached;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = $"M_{key}", color = color };
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            if (emission)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2.2f);
            }
            Materials[key] = material;
            return material;
        }

        private static GameObject CreatePart(Transform parent, PrimitiveType type, string name, Vector3 position,
            Vector3 scale, Vector3 rotation, Material material, bool collider = false)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localEulerAngles = rotation;
            part.transform.localScale = scale;
            if (part.TryGetComponent<Renderer>(out var renderer)) renderer.sharedMaterial = material;
            if (!collider && part.TryGetComponent<Collider>(out var partCollider)) UnityEngine.Object.Destroy(partCollider);
            return part;
        }

        private static int StableHash(string value)
        {
            unchecked
            {
                var hash = 17;
                foreach (var c in value ?? string.Empty) hash = hash * 31 + c;
                return hash;
            }
        }

        private static bool TryCreateCatalogShip(string shipId, Color factionColor, bool hostile, out GameObject ship)
        {
            ship = null;
            if (!VisualCatalog || !VisualCatalog.TryGetShip(shipId, out var entry) || !entry.Prefab) return false;

            ship = UnityEngine.Object.Instantiate(entry.Prefab);
            ship.name = $"Ship_{shipId}";
            if (hostile) ApplyHostileTint(ship, factionColor);
            return true;
        }

        private static void ApplyHostileTint(GameObject root, Color factionColor)
        {
            var hostileColor = Color.Lerp(new Color(0.75f, 0.08f, 0.1f, 1f), factionColor, 0.25f);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", hostileColor);
                block.SetColor("_Color", hostileColor);
                renderer.SetPropertyBlock(block);
            }
        }

        private static void ApplyAccentTint(GameObject root, Color color)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.transform.name.Contains("FactionAccent", StringComparison.Ordinal)) continue;
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                block.SetColor("_EmissionColor", color * 2.5f);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
