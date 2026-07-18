using UnityEngine;

namespace Starfall.UI
{
    public interface IMobileBackHandler
    {
        bool HandleMobileBack();
    }

    public static class MobileBackNavigation
    {
        public static IMobileBackHandler Current { get; set; }

        public static bool HandleBack()
        {
            return Current != null && Current.HandleMobileBack();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Current = null;
        }
    }
}
