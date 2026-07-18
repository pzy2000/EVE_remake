using UnityEngine;

namespace Starfall.Presentation
{
    [CreateAssetMenu(fileName = "MusicCatalog", menuName = "STARFALL ODYSSEY/Music Catalog")]
    public sealed class MusicCatalogAsset : ScriptableObject
    {
        [SerializeField] private AudioClip mainMenuTrack;
        [SerializeField] private AudioClip[] spacePlaylist = System.Array.Empty<AudioClip>();
        [SerializeField] private AudioClip stationTrack;

        public AudioClip MainMenuTrack => mainMenuTrack;
        public AudioClip[] SpacePlaylist => spacePlaylist;
        public AudioClip StationTrack => stationTrack;

        public void Configure(AudioClip menu, AudioClip[] space, AudioClip station)
        {
            mainMenuTrack = menu;
            spacePlaylist = space ?? System.Array.Empty<AudioClip>();
            stationTrack = station;
        }
    }
}
