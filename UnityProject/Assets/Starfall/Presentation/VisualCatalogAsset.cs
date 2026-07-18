using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.Presentation
{
    [CreateAssetMenu(fileName = "VisualCatalog", menuName = "Starfall/Visual Catalog")]
    public sealed class VisualCatalogAsset : ScriptableObject
    {
        [SerializeField] private int generationVersion;
        [SerializeField] private ShipVisualEntry[] ships = Array.Empty<ShipVisualEntry>();
        [SerializeField] private GameObject stationPrefab;
        [SerializeField] private GameObject gatePrefab;

        [NonSerialized] private Dictionary<string, ShipVisualEntry> shipLookup;

        public int GenerationVersion => generationVersion;
        public IReadOnlyList<ShipVisualEntry> Ships => ships;
        public GameObject StationPrefab => stationPrefab;
        public GameObject GatePrefab => gatePrefab;

        public bool TryGetShip(string stableId, out ShipVisualEntry entry)
        {
            if (shipLookup == null) RebuildLookup();
            return shipLookup.TryGetValue(stableId ?? string.Empty, out entry);
        }

        private void OnEnable()
        {
            RebuildLookup();
        }

        private void OnValidate()
        {
            RebuildLookup();
        }

        private void RebuildLookup()
        {
            shipLookup = new Dictionary<string, ShipVisualEntry>(StringComparer.Ordinal);
            foreach (var entry in ships ?? Array.Empty<ShipVisualEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.StableId)) continue;
                shipLookup[entry.StableId] = entry;
            }
        }

#if UNITY_EDITOR
        public void ConfigureGenerated(int version, ShipVisualEntry[] generatedShips,
            GameObject generatedStationPrefab, GameObject generatedGatePrefab)
        {
            generationVersion = version;
            ships = generatedShips ?? Array.Empty<ShipVisualEntry>();
            stationPrefab = generatedStationPrefab;
            gatePrefab = generatedGatePrefab;
            RebuildLookup();
        }
#endif
    }

    [Serializable]
    public sealed class ShipVisualEntry
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private string factionId = string.Empty;
        [SerializeField] private string shipClass = string.Empty;
        [SerializeField] private string sourceModel = string.Empty;
        [SerializeField] private GameObject prefab;
        [SerializeField] private Sprite thumbnail;

        public string StableId => stableId;
        public string FactionId => factionId;
        public string ShipClass => shipClass;
        public string SourceModel => sourceModel;
        public GameObject Prefab => prefab;
        public Sprite Thumbnail => thumbnail;

        public ShipVisualEntry(string id, string faction, string className, string source, GameObject visualPrefab)
            : this(id, faction, className, source, visualPrefab, null)
        {
        }

        public ShipVisualEntry(string id, string faction, string className, string source,
            GameObject visualPrefab, Sprite visualThumbnail)
        {
            stableId = id ?? string.Empty;
            factionId = faction ?? string.Empty;
            shipClass = className ?? string.Empty;
            sourceModel = source ?? string.Empty;
            prefab = visualPrefab;
            thumbnail = visualThumbnail;
        }

#if UNITY_EDITOR
        public void SetThumbnail(Sprite visualThumbnail)
        {
            thumbnail = visualThumbnail;
        }
#endif
    }

}
