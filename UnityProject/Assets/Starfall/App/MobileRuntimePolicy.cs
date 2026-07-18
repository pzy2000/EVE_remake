using System;
using System.Collections.Generic;

namespace Starfall.App
{
    public static class QualityLevelPolicy
    {
        public static int Next(string[] names, int current, bool mobile)
        {
            var allowed = Allowed(names, mobile);
            if (allowed.Count == 0) return 0;
            var position = allowed.IndexOf(current);
            return position < 0 || position >= allowed.Count - 1 ? allowed[0] : allowed[position + 1];
        }

        public static int ResolvePersisted(string[] names, int persisted, int fallback, bool mobile)
        {
            var allowed = Allowed(names, mobile);
            if (allowed.Count == 0) return 0;
            if (allowed.Contains(persisted)) return persisted;
            return allowed.Contains(fallback) ? fallback : allowed[0];
        }

        public static List<int> Allowed(string[] names, bool mobile)
        {
            var result = new List<int>();
            if (names == null || names.Length == 0) return result;
            if (!mobile)
            {
                var desktop = Array.FindIndex(names,
                    name => string.Equals(name, "PC", StringComparison.OrdinalIgnoreCase));
                result.Add(desktop >= 0 ? desktop : names.Length - 1);
                return result;
            }
            for (var i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], "PC", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(i);
            }
            if (result.Count == 0)
                for (var i = 0; i < names.Length; i++) result.Add(i);
            return result;
        }
    }

    /// <summary>Deduplicates Unity's paired pause/focus callbacks into foreground edges.</summary>
    public sealed class LifecycleSaveCoordinator
    {
        private bool paused;
        private bool focused = true;
        private bool background;

        public bool SetPaused(bool value, Action save, Action<Exception> onError = null)
        {
            paused = value;
            return Reevaluate(save, onError);
        }

        public bool SetFocused(bool value, Action save, Action<Exception> onError = null)
        {
            focused = value;
            return Reevaluate(save, onError);
        }

        private bool Reevaluate(Action save, Action<Exception> onError)
        {
            var nextBackground = paused || !focused;
            if (nextBackground == background) return false;
            background = nextBackground;
            if (!background || save == null) return false;
            try
            {
                save();
            }
            catch (Exception exception)
            {
                onError?.Invoke(exception);
            }
            return true;
        }
    }
}
