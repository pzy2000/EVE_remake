using UnityEngine;

namespace Starfall.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ShipVisualIdentity : MonoBehaviour
    {
        [SerializeField] private string stableId = string.Empty;
        [SerializeField] private string factionId = string.Empty;
        [SerializeField] private string shipClass = string.Empty;
        [SerializeField] private string sourceModel = string.Empty;
        [SerializeField] private Transform weaponHardpoint;
        [SerializeField] private Transform engineHardpoint;

        public string StableId => stableId;
        public string FactionId => factionId;
        public string ShipClass => shipClass;
        public string SourceModel => sourceModel;
        public Transform WeaponHardpoint => weaponHardpoint;
        public Transform EngineHardpoint => engineHardpoint;

#if UNITY_EDITOR
        public void Configure(string id, string faction, string className, string source,
            Transform weapon, Transform engine)
        {
            stableId = id ?? string.Empty;
            factionId = faction ?? string.Empty;
            shipClass = className ?? string.Empty;
            sourceModel = source ?? string.Empty;
            weaponHardpoint = weapon;
            engineHardpoint = engine;
        }
#endif
    }
}
