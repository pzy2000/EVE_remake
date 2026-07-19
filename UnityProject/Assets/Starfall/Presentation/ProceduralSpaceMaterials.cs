using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starfall.Presentation
{
    public readonly struct SystemSkyStyle
    {
        public SystemSkyStyle(string systemId, string factionId, int seed, Color background,
            Color nebulaA, Color nebulaB, Color starA, Color starB, Color keyLight,
            Color ambientSky, Color ambientEquator, float nebulaStrength, float ribbonWidth,
            float patternFrequency, int starCount, float rotationDegrees, int pattern)
        {
            SystemId = systemId;
            FactionId = factionId;
            Seed = seed;
            Background = background;
            NebulaA = nebulaA;
            NebulaB = nebulaB;
            StarA = starA;
            StarB = starB;
            KeyLight = keyLight;
            AmbientSky = ambientSky;
            AmbientEquator = ambientEquator;
            NebulaStrength = nebulaStrength;
            RibbonWidth = ribbonWidth;
            PatternFrequency = patternFrequency;
            StarCount = starCount;
            RotationDegrees = rotationDegrees;
            Pattern = pattern;
        }

        public string SystemId { get; }
        public string FactionId { get; }
        public int Seed { get; }
        public Color Background { get; }
        public Color NebulaA { get; }
        public Color NebulaB { get; }
        public Color StarA { get; }
        public Color StarB { get; }
        public Color KeyLight { get; }
        public Color AmbientSky { get; }
        public Color AmbientEquator { get; }
        public float NebulaStrength { get; }
        public float RibbonWidth { get; }
        public float PatternFrequency { get; }
        public int StarCount { get; }
        public float RotationDegrees { get; }
        public int Pattern { get; }
    }

    public static class ProceduralSpaceMaterials
    {
        private const int MaxCachedSkies = 8;
        private static readonly Dictionary<string, Material> Materials = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);
        private static readonly Queue<string> SkyCacheKeys = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Materials.Clear();
            Textures.Clear();
            SkyCacheKeys.Clear();
        }

        public static int ReleaseUnusedRuntimeCaches()
        {
            var usedMaterials = new HashSet<Material>();
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var material in renderer.sharedMaterials)
                    if (material) usedMaterials.Add(material);

            var staleMaterials = new List<string>();
            foreach (var pair in Materials)
                if (!pair.Value || !usedMaterials.Contains(pair.Value)) staleMaterials.Add(pair.Key);
            foreach (var key in staleMaterials)
            {
                if (Materials.Remove(key, out var material) && material)
                    UnityEngine.Object.Destroy(material);
            }

            var usedTextures = new HashSet<Texture>();
            foreach (var material in Materials.Values)
            {
                if (!material) continue;
                foreach (var propertyName in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(propertyName);
                    if (texture) usedTextures.Add(texture);
                }
            }
            var staleTextures = new List<string>();
            foreach (var pair in Textures)
                if (!pair.Value || !usedTextures.Contains(pair.Value)) staleTextures.Add(pair.Key);
            foreach (var key in staleTextures)
            {
                if (Textures.Remove(key, out var texture) && texture)
                    UnityEngine.Object.Destroy(texture);
            }

            SkyCacheKeys.Clear();
            foreach (var key in Materials.Keys)
                if (key.StartsWith("sky-", StringComparison.Ordinal)) SkyCacheKeys.Enqueue(key);
            return staleMaterials.Count + staleTextures.Count;
        }

        public static GameObject CreateSkyDome(Transform parent, string key, int seed, Color nebulaA, Color nebulaB)
        {
            var style = CreateGenericStyle(key, seed, nebulaA, nebulaB);
            return CreateSkyDome(parent, key, style);
        }

        public static GameObject CreateSystemSkyDome(Transform parent, SystemSkyStyle style)
        {
            return CreateSkyDome(parent, $"system-{style.SystemId}-{style.FactionId}", style);
        }

        public static SystemSkyStyle GetSystemSkyStyle(string systemId, string factionId,
            Color factionColor, float security)
        {
            var safeSystemId = string.IsNullOrWhiteSpace(systemId) ? "unknown" : systemId;
            var safeFactionId = string.IsNullOrWhiteSpace(factionId) ? "unclaimed" : factionId;
            var seed = StableSeed(safeSystemId);
            var variation = ((uint)seed >> 8 & 255u) / 255f;
            var security01 = Mathf.InverseLerp(-0.1f, 1f, security);

            Color background;
            Color nebulaA;
            Color nebulaB;
            Color starA;
            Color starB;
            float strength;
            float width;
            float frequency;
            int stars;
            int pattern;

            switch (safeFactionId)
            {
                case "aurelian":
                    background = new Color(0.014f, 0.006f, 0.018f);
                    nebulaA = new Color(0.92f, 0.48f, 0.055f);
                    nebulaB = new Color(0.42f, 0.055f, 0.10f);
                    starA = new Color(1f, 0.87f, 0.48f);
                    starB = new Color(1f, 0.96f, 0.82f);
                    strength = 1.05f; width = 0.15f; frequency = 3.2f; stars = 1580; pattern = 0;
                    break;
                case "kaldari":
                    background = new Color(0.002f, 0.009f, 0.027f);
                    nebulaA = new Color(0.055f, 0.32f, 0.90f);
                    nebulaB = new Color(0.04f, 0.72f, 0.90f);
                    starA = new Color(0.55f, 0.78f, 1f);
                    starB = new Color(0.86f, 0.95f, 1f);
                    strength = 0.92f; width = 0.12f; frequency = 5.2f; stars = 1950; pattern = 1;
                    break;
                case "meridian":
                    background = new Color(0.002f, 0.018f, 0.023f);
                    nebulaA = new Color(0.035f, 0.76f, 0.62f);
                    nebulaB = new Color(0.28f, 0.08f, 0.56f);
                    starA = new Color(0.55f, 1f, 0.84f);
                    starB = new Color(0.78f, 0.82f, 1f);
                    strength = 1.08f; width = 0.20f; frequency = 4.1f; stars = 1700; pattern = 2;
                    break;
                case "varkhald":
                    background = new Color(0.022f, 0.006f, 0.003f);
                    nebulaA = new Color(0.78f, 0.19f, 0.045f);
                    nebulaB = new Color(0.34f, 0.20f, 0.035f);
                    starA = new Color(1f, 0.56f, 0.30f);
                    starB = new Color(1f, 0.84f, 0.62f);
                    strength = 1.12f; width = 0.25f; frequency = 6f; stars = 1420; pattern = 3;
                    break;
                case "blood_reavers":
                    background = new Color(0.018f, 0.001f, 0.006f);
                    nebulaA = new Color(0.68f, 0.015f, 0.06f);
                    nebulaB = new Color(0.18f, 0.012f, 0.025f);
                    starA = new Color(1f, 0.35f, 0.30f);
                    starB = new Color(0.78f, 0.70f, 0.66f);
                    strength = 1.18f; width = 0.18f; frequency = 7.2f; stars = 1120; pattern = 3;
                    break;
                case "nathari":
                    background = new Color(0.008f, 0.002f, 0.022f);
                    nebulaA = new Color(0.40f, 0.06f, 0.74f);
                    nebulaB = new Color(0.02f, 0.58f, 0.72f);
                    starA = new Color(0.72f, 0.48f, 1f);
                    starB = new Color(0.46f, 0.94f, 1f);
                    strength = 1.1f; width = 0.13f; frequency = 8f; stars = 1380; pattern = 1;
                    break;
                case "crimson_hand":
                    background = new Color(0.020f, 0.002f, 0.014f);
                    nebulaA = new Color(0.82f, 0.025f, 0.24f);
                    nebulaB = new Color(0.36f, 0.025f, 0.46f);
                    starA = new Color(1f, 0.42f, 0.60f);
                    starB = new Color(0.86f, 0.70f, 1f);
                    strength = 1.14f; width = 0.17f; frequency = 6.8f; stars = 1260; pattern = 2;
                    break;
                case "ashfang":
                    background = new Color(0.015f, 0.012f, 0.002f);
                    nebulaA = new Color(0.50f, 0.42f, 0.045f);
                    nebulaB = new Color(0.43f, 0.11f, 0.025f);
                    starA = new Color(0.88f, 0.86f, 0.40f);
                    starB = new Color(1f, 0.56f, 0.28f);
                    strength = 1.04f; width = 0.28f; frequency = 5.7f; stars = 1180; pattern = 3;
                    break;
                case "sisters":
                    background = new Color(0.008f, 0.014f, 0.025f);
                    nebulaA = new Color(0.68f, 0.80f, 0.92f);
                    nebulaB = new Color(0.27f, 0.50f, 0.62f);
                    starA = new Color(1f, 0.98f, 0.90f);
                    starB = new Color(0.72f, 0.88f, 1f);
                    strength = 0.72f; width = 0.24f; frequency = 2.6f; stars = 2050; pattern = 0;
                    break;
                case "directorate":
                    background = new Color(0.004f, 0.009f, 0.020f);
                    nebulaA = new Color(0.70f, 0.78f, 0.86f);
                    nebulaB = new Color(0.88f, 0.56f, 0.04f);
                    starA = new Color(0.90f, 0.96f, 1f);
                    starB = new Color(1f, 0.78f, 0.28f);
                    strength = 0.82f; width = 0.10f; frequency = 4.8f; stars = 1900; pattern = 1;
                    break;
                default:
                    background = Color.Lerp(new Color(0.002f, 0.006f, 0.018f), factionColor * 0.055f, 0.55f);
                    nebulaA = Color.Lerp(factionColor, Color.white, 0.08f);
                    nebulaB = Color.Lerp(factionColor, new Color(0.24f, 0.06f, 0.42f), 0.45f);
                    starA = Color.Lerp(factionColor, Color.white, 0.52f);
                    starB = Color.white;
                    strength = 0.94f; width = 0.19f; frequency = 4.4f; stars = 1600; pattern = 2;
                    break;
            }

            var colorShift = Mathf.Lerp(0.90f, 1.10f, variation);
            nebulaA *= colorShift;
            nebulaB *= Mathf.Lerp(1.08f, 0.91f, variation);
            strength *= Mathf.Lerp(1.12f, 0.86f, security01) * Mathf.Lerp(0.92f, 1.08f, variation);
            width *= Mathf.Lerp(0.86f, 1.14f, 1f - variation);
            frequency *= Mathf.Lerp(0.91f, 1.12f, variation);
            stars += Mathf.RoundToInt(Mathf.Lerp(-160f, 180f, variation));
            background *= Mathf.Lerp(0.66f, 1.04f, security01);

            return new SystemSkyStyle(safeSystemId, safeFactionId, seed, background,
                nebulaA, nebulaB, starA, starB,
                Color.Lerp(new Color(0.62f, 0.72f, 0.88f), starA, 0.30f),
                Color.Lerp(background * 2.8f, nebulaA * 0.12f, 0.45f),
                Color.Lerp(background * 1.7f, nebulaB * 0.08f, 0.35f),
                strength, width, frequency, stars, PositiveModulo(seed, 360), pattern);
        }

        public static Material GetPlanetMaterial(string id, Color baseColor, bool moon)
        {
            var key = $"planet-{id}-{ColorUtility.ToHtmlStringRGB(baseColor)}-{moon}";
            if (Materials.TryGetValue(key, out var cached) && cached) return cached;

            var shader = FindRuntimeSurfaceShader();
            var material = new Material(shader) { name = $"M_{key}" };
            var texture = CreatePlanetTexture(key, StableSeed(id), baseColor, moon);
            SetBaseTexture(material, texture);
            SetBaseColor(material, Color.white);
            SetFloatIfPresent(material, "_Smoothness", moon ? 0.16f : 0.42f);
            SetFloatIfPresent(material, "_Metallic", moon ? 0.02f : 0.06f);
            Materials[key] = material;
            return material;
        }

        public static Material GetAsteroidMaterial()
        {
            const string key = "asteroid-rock-v2";
            if (Materials.TryGetValue(key, out var cached) && cached) return cached;

            var shader = FindRuntimeSurfaceShader();
            var material = new Material(shader) { name = "M_AsteroidRockV2" };
            SetBaseTexture(material, CreateRockTexture(key));
            SetBaseColor(material, Color.white);
            SetFloatIfPresent(material, "_Smoothness", 0.08f);
            SetFloatIfPresent(material, "_Metallic", 0.03f);
            Materials[key] = material;
            return material;
        }

        private static Shader FindRuntimeSurfaceShader()
        {
#if STARFALL_ANDROID_CI && UNITY_ANDROID
            return Shader.Find("Starfall/CI/MinimalUnlit") ??
                   Shader.Find("Universal Render Pipeline/Unlit") ??
                   Shader.Find("Sprites/Default");
#else
            return Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Standard") ??
                   Shader.Find("Sprites/Default");
#endif
        }

        public static Material GetGlowMaterial(string key, Color color, float intensity = 1.5f)
        {
            var cacheKey = $"glow-{key}-{ColorUtility.ToHtmlStringRGB(color)}";
            if (Materials.TryGetValue(cacheKey, out var cached) && cached) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = $"M_{cacheKey}" };
            var finalColor = color * intensity;
            SetBaseColor(material, finalColor);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", finalColor);
            Materials[cacheKey] = material;
            return material;
        }

        private static GameObject CreateSkyDome(Transform parent, string key, SystemSkyStyle style)
        {
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = $"Nebula Sky Dome · {style.FactionId} · {style.SystemId}";
            dome.transform.SetParent(parent, false);
            dome.transform.localPosition = Vector3.zero;
            dome.transform.localRotation = Quaternion.Euler(0f, style.RotationDegrees, 0f);
            dome.transform.localScale = Vector3.one * 1600f;
            dome.GetComponent<Renderer>().sharedMaterial = GetSkyMaterial(key, style);
            if (dome.TryGetComponent<Collider>(out var collider)) UnityEngine.Object.Destroy(collider);
            return dome;
        }

        private static Material GetSkyMaterial(string key, SystemSkyStyle style)
        {
            var cacheKey = $"sky-{key}-{style.Seed}";
            if (Materials.TryGetValue(cacheKey, out var cached) && cached) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Texture") ??
                         Shader.Find("Sprites/Default");
            var material = new Material(shader) { name = $"M_{cacheKey}", renderQueue = 1000 };
            SetBaseTexture(material, CreateSkyTexture(cacheKey, style));
            SetBaseColor(material, Color.white);
            SetFloatIfPresent(material, "_Cull", (float)CullMode.Front);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            Materials[cacheKey] = material;
            SkyCacheKeys.Enqueue(cacheKey);
            TrimSkyCache();
            return material;
        }

        private static Texture2D CreateSkyTexture(string key, SystemSkyStyle style)
        {
            if (Textures.TryGetValue(key, out var cached) && cached) return cached;
            const int width = 1536;
            const int height = 768;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = $"T_{key}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[width * height];
            var phase = (style.Seed & 1023) * 0.006135923f;

            for (var y = 0; y < height; y++)
            {
                var v = y / (float)(height - 1);
                for (var x = 0; x < width; x++)
                {
                    var u = x / (float)(width - 1);
                    var patternOffset = style.Pattern * 0.73f;
                    // Every horizontal term must use an integer number of full 2π
                    // cycles so the equirectangular texture closes cleanly on a sphere.
                    var uAngle = u * Mathf.PI * 2f;
                    var cycleCount = Mathf.Max(2, Mathf.RoundToInt(style.PatternFrequency));
                    var centerWave = style.Pattern switch
                    {
                        0 => Mathf.Sin(uAngle * cycleCount + phase) * 0.11f,
                        1 => Mathf.Sin(uAngle * cycleCount + phase) * 0.06f +
                             Mathf.Sin(uAngle * (cycleCount + 4) - phase) * 0.035f,
                        2 => Mathf.Sin(uAngle * cycleCount + phase) * 0.18f,
                        _ => Mathf.Sin(uAngle * cycleCount + phase) * 0.13f +
                             Mathf.Sin(uAngle * (cycleCount + 6) + phase * 0.4f) * 0.055f,
                    };
                    var ribbonCenter = 0.48f + centerWave;
                    var ribbon = Mathf.Exp(-Mathf.Pow((v - ribbonCenter) / style.RibbonWidth, 2f));
                    var waveA = Mathf.Sin(uAngle * (cycleCount + 2) +
                                         Mathf.Sin(v * (13f + style.Pattern * 3f) + phase) * 2.8f + phase);
                    var waveB = Mathf.Sin(v * (36f + style.Pattern * 7f) -
                                         uAngle * (3 + style.Pattern) + phase * 1.7f);
                    var waveC = Mathf.Sin(uAngle * (cycleCount + 7 + style.Pattern) +
                                         v * (61f + style.Pattern * 9f) + phase * 3.1f + patternOffset);
                    var turbulence = Mathf.Clamp01(0.5f + waveA * 0.24f + waveB * 0.17f + waveC * 0.09f);
                    var nebula = Mathf.Clamp01((ribbon * (0.18f + turbulence * 0.92f) - 0.11f) * style.NebulaStrength);
                    var color = style.Background;
                    color += style.NebulaA * (nebula * 0.27f);
                    color += style.NebulaB * (nebula * nebula * 0.21f);
                    color += new Color(0.005f, 0.012f, 0.025f) * Mathf.Clamp01((1f - v) * 0.2f);
                    color.a = 1f;
                    pixels[y * width + x] = color;
                }
            }

            var random = new System.Random(style.Seed);
            for (var i = 0; i < style.StarCount; i++)
            {
                var x = random.Next(width);
                var y = random.Next(height);
                var brightness = Mathf.Lerp(0.24f, 0.82f, (float)random.NextDouble());
                var tintRoll = random.Next(4);
                var tint = tintRoll == 0 ? style.StarA : tintRoll == 1 ? style.StarB : Color.white;
                pixels[y * width + x] = tint * brightness;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            Textures[key] = texture;
            return texture;
        }

        private static Texture2D CreatePlanetTexture(string key, int seed, Color baseColor, bool moon)
        {
            if (Textures.TryGetValue(key, out var cached) && cached) return cached;
            const int width = 256;
            const int height = 128;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, false)
            {
                name = $"T_{key}",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };
            var pixels = new Color[width * height];
            var phase = (seed & 2047) * 0.0030679615f;
            var neutral = new Color(0.48f, 0.51f, 0.55f);
            var bodyColor = moon ? Color.Lerp(baseColor, neutral, 0.72f) : baseColor;

            for (var y = 0; y < height; y++)
            {
                var v = y / (float)(height - 1);
                for (var x = 0; x < width; x++)
                {
                    var u = x / (float)(width - 1);
                    var bands = Mathf.Sin(v * (moon ? 38f : 24f) + Mathf.Sin(u * 17f + phase) * 1.8f + phase);
                    var detail = Mathf.Sin(u * 61f - v * 27f + phase * 2f) * 0.5f +
                                 Mathf.Sin(u * 113f + v * 79f + phase) * 0.25f;
                    var shade = moon
                        ? 0.66f + bands * 0.08f + detail * 0.09f
                        : 0.72f + bands * 0.13f + detail * 0.08f;
                    var color = bodyColor * Mathf.Clamp(shade, 0.38f, 1.08f);
                    color.a = 1f;
                    pixels[y * width + x] = color;
                }
            }

            if (moon)
            {
                var random = new System.Random(seed ^ 0x51f15e);
                for (var crater = 0; crater < 24; crater++)
                {
                    var centerX = random.Next(width);
                    var centerY = random.Next(height);
                    var radius = random.Next(2, 11);
                    for (var oy = -radius; oy <= radius; oy++)
                    for (var ox = -radius; ox <= radius; ox++)
                    {
                        var distance = Mathf.Sqrt(ox * ox + oy * oy) / radius;
                        if (distance > 1f) continue;
                        var px = (centerX + ox + width) % width;
                        var py = Mathf.Clamp(centerY + oy, 0, height - 1);
                        var darken = 1f - (1f - distance) * 0.28f;
                        pixels[py * width + px] *= darken;
                    }
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            Textures[key] = texture;
            return texture;
        }

        private static Texture2D CreateRockTexture(string key)
        {
            if (Textures.TryGetValue(key, out var cached) && cached) return cached;
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
            {
                name = "T_AsteroidRockV2",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear
            };
            var pixels = new Color[size * size];
            var baseColor = new Color(0.25f, 0.20f, 0.155f);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var n = Mathf.Sin(x * 0.31f + Mathf.Sin(y * 0.17f) * 2f) * 0.20f +
                        Mathf.Sin(y * 0.43f - x * 0.11f) * 0.13f +
                        Mathf.Sin((x + y) * 0.79f) * 0.05f;
                var color = baseColor * Mathf.Clamp(0.78f + n, 0.42f, 1.08f);
                color.a = 1f;
                pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            Textures[key] = texture;
            return texture;
        }

        private static SystemSkyStyle CreateGenericStyle(string key, int seed, Color nebulaA, Color nebulaB)
        {
            return new SystemSkyStyle(key, "neutral", seed,
                new Color(0.0025f, 0.006f, 0.018f), nebulaA, nebulaB,
                new Color(0.62f, 0.78f, 1f), new Color(1f, 0.86f, 0.62f),
                new Color(0.72f, 0.82f, 1f), new Color(0.025f, 0.055f, 0.12f),
                new Color(0.015f, 0.025f, 0.055f), 1f, 0.19f, 4f, 1700,
                PositiveModulo(seed, 360), 2);
        }

        private static void TrimSkyCache()
        {
            while (SkyCacheKeys.Count > MaxCachedSkies)
            {
                var key = SkyCacheKeys.Dequeue();
                if (Materials.Remove(key, out var material) && material)
                    UnityEngine.Object.Destroy(material);
                if (Textures.Remove(key, out var texture) && texture)
                    UnityEngine.Object.Destroy(texture);
            }
        }

        private static int PositiveModulo(int value, int divisor)
        {
            var result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static int StableSeed(string value)
        {
            unchecked
            {
                var hash = 23;
                foreach (var character in value ?? string.Empty) hash = hash * 31 + character;
                return hash;
            }
        }

        private static void SetBaseTexture(Material material, Texture texture)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        }

        private static void SetBaseColor(Material material, Color color)
        {
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static void SetFloatIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }
}
