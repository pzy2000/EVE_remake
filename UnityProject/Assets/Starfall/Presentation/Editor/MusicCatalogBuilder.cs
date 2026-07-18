using System.IO;
using UnityEditor;
using UnityEngine;

namespace Starfall.Presentation.Editor
{
    public static class MusicCatalogBuilder
    {
        private const string CatalogPath = "Assets/Starfall/Presentation/Resources/MusicCatalog.asset";
        private const string AudioRoot = "Assets/Starfall/Audio/ThirdParty/OpenGameArt";
        private static readonly string[] AudioPaths =
        {
            AudioRoot + "/Heavenly Loop.ogg",
            AudioRoot + "/Exploration Theme.ogg",
            AudioRoot + "/Magic Space.mp3",
            AudioRoot + "/Outer Space Loop.mp3",
            AudioRoot + "/Ambient Relaxing Loop.ogg",
        };

        [MenuItem("Tools/STARFALL ODYSSEY/Rebuild Music Catalog")]
        public static void Rebuild()
        {
            foreach (var path in AudioPaths) ConfigureImporter(path);

            var clips = new AudioClip[AudioPaths.Length];
            for (var i = 0; i < AudioPaths.Length; i++)
            {
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioPaths[i]);
                if (!clips[i])
                {
                    Debug.LogError($"[Starfall Music] Required audio clip is missing: {AudioPaths[i]}");
                    return;
                }
            }

            EnsureFolder(Path.GetDirectoryName(CatalogPath)?.Replace('\\', '/'));
            var catalog = AssetDatabase.LoadAssetAtPath<MusicCatalogAsset>(CatalogPath);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<MusicCatalogAsset>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.Configure(clips[0], new[] { clips[1], clips[2], clips[3] }, clips[4]);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[Starfall Music] Music catalog rebuilt with 5 CC0 tracks.", catalog);
        }

        private static void ConfigureImporter(string path)
        {
            if (AssetImporter.GetAtPath(path) is not AudioImporter importer) return;

            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = true;
            importer.SaveAndReimport();
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
