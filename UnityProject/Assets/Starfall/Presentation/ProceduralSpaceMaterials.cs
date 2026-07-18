using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starfall.Presentation
{
    public static class ProceduralSpaceMaterials
    {
        private static readonly Dictionary<string, Material> Materials = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Materials.Clear();
            Textures.Clear();
        }

        public static GameObject CreateSkyDome(Transform parent, string key, int seed, Color nebulaA, Color nebulaB)
        {
            var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Nebula Sky Dome";
            dome.transform.SetParent(parent, false);
            dome.transform.localPosition = Vector3.zero;
            dome.transform.localRotation = Quaternion.Euler(0f, seed % 360, 0f);
            dome.transform.localScale = Vector3.one * 1600f;
            dome.GetComponent<Renderer>().sharedMaterial = GetSkyMaterial(key, seed, nebulaA, nebulaB);
            if (dome.TryGetComponent<Collider>(out var collider)) UnityEngine.Object.Destroy(collider);
            return dome;
        }

        public static Material GetPlanetMaterial(string id, Color baseColor, bool moon)
        {
            var key = $"planet-{id}-{ColorUtility.ToHtmlStringRGB(baseColor)}-{moon}";
            if (Materials.TryGetValue(key, out var cached) && cached) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
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

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "M_AsteroidRockV2" };
            SetBaseTexture(material, CreateRockTexture(key));
            SetBaseColor(material, Color.white);
            SetFloatIfPresent(material, "_Smoothness", 0.08f);
            SetFloatIfPresent(material, "_Metallic", 0.03f);
            Materials[key] = material;
            return material;
        }

        public static Material GetGlowMaterial(string key, Color color, float intensity = 1.5f)
        {
            var cacheKey = $"glow-{key}-{ColorUtility.ToHtmlStringRGB(color)}";
            if (Materials.TryGetValue(cacheKey, out var cached) && cached) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = $"M_{cacheKey}" };
            var finalColor = color * intensity;
            SetBaseColor(material, finalColor);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", finalColor);
            Materials[cacheKey] = material;
            return material;
        }

        private static Material GetSkyMaterial(string key, int seed, Color nebulaA, Color nebulaB)
        {
            var cacheKey = $"sky-{key}-{seed}";
            if (Materials.TryGetValue(cacheKey, out var cached) && cached) return cached;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
            var material = new Material(shader) { name = $"M_{cacheKey}", renderQueue = 1000 };
            SetBaseTexture(material, CreateSkyTexture(cacheKey, seed, nebulaA, nebulaB));
            SetBaseColor(material, Color.white);
            SetFloatIfPresent(material, "_Cull", (float)CullMode.Front);
            SetFloatIfPresent(material, "_ZWrite", 0f);
            Materials[cacheKey] = material;
            return material;
        }

        private static Texture2D CreateSkyTexture(string key, int seed, Color nebulaA, Color nebulaB)
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
            var phase = (seed & 1023) * 0.006135923f;
            var background = new Color(0.0025f, 0.006f, 0.018f, 1f);

            for (var y = 0; y < height; y++)
            {
                var v = y / (float)(height - 1);
                for (var x = 0; x < width; x++)
                {
                    var u = x / (float)(width - 1);
                    var ribbonCenter = 0.48f + Mathf.Sin(u * Mathf.PI * 4f + phase) * 0.16f;
                    var ribbon = Mathf.Exp(-Mathf.Pow((v - ribbonCenter) / 0.19f, 2f));
                    var waveA = Mathf.Sin(u * Mathf.PI * 10f + Mathf.Sin(v * 15f + phase) * 2.8f + phase);
                    var waveB = Mathf.Sin(v * 43f - u * Mathf.PI * 6f + phase * 1.7f);
                    var waveC = Mathf.Sin(u * Mathf.PI * 22f + v * 73f + phase * 3.1f);
                    var turbulence = Mathf.Clamp01(0.5f + waveA * 0.24f + waveB * 0.17f + waveC * 0.09f);
                    var nebula = Mathf.Clamp01(ribbon * (0.18f + turbulence * 0.92f) - 0.11f);
                    var color = background;
                    color += nebulaA * (nebula * 0.27f);
                    color += nebulaB * (nebula * nebula * 0.21f);
                    color += new Color(0.005f, 0.012f, 0.025f) * Mathf.Clamp01((1f - v) * 0.2f);
                    color.a = 1f;
                    pixels[y * width + x] = color;
                }
            }

            var random = new System.Random(seed);
            for (var i = 0; i < 1700; i++)
            {
                var x = random.Next(width);
                var y = random.Next(height);
                var brightness = Mathf.Lerp(0.24f, 0.82f, (float)random.NextDouble());
                var tintRoll = random.Next(3);
                var tint = tintRoll switch
                {
                    0 => new Color(0.62f, 0.78f, 1f),
                    1 => new Color(1f, 0.86f, 0.62f),
                    _ => Color.white
                };
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
