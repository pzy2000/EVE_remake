#if STARFALL_ANDROID_CI
namespace Starfall.Simulation
{
    /// <summary>Smoke-player-only deterministic state fixture.</summary>
    public sealed partial class GameSession
    {
        public void ShowDeathOverlayForAndroidCi()
        {
            State.PlayerDead = true;
        }
    }
}
#endif
