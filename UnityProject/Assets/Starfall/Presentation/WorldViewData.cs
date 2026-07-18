using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starfall.Presentation
{
    public enum WorldViewKind
    {
        Star,
        Planet,
        Moon,
        Station,
        Gate,
        Belt,
        Asteroid,
        Ship,
        Beacon,
        Projectile
    }

    [Serializable]
    public sealed class WorldObjectViewData
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public WorldViewKind Kind;
        public Vector3 Position;
        public float Radius = 1f;
        public Color Color = Color.white;
        public string ShipId = string.Empty;
        public string ShipClass = "frigate";
        public string FactionId = string.Empty;
        public bool IsPlayer;
        public bool IsHostile;
        public float HeadingDegrees;
        public float Shield01 = 1f;
        public float Armor01 = 1f;
        public float Hull01 = 1f;
    }

    [Serializable]
    public sealed class SpaceSnapshot
    {
        public string SystemId = string.Empty;
        public string SystemName = "Unknown";
        public float Security;
        public Color FactionColor = new(0.16f, 0.8f, 1f, 1f);
        public string SelectedId = string.Empty;
        public readonly List<WorldObjectViewData> Objects = new();
    }

    public sealed class SelectableView : MonoBehaviour
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private WorldViewKind kind;

        public string StableId => stableId;
        public string DisplayName => displayName;
        public WorldViewKind Kind => kind;

        public void Configure(WorldObjectViewData data)
        {
            stableId = data.Id;
            displayName = data.Name;
            kind = data.Kind;
        }
    }
}
