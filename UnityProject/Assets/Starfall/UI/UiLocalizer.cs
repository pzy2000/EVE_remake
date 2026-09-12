using System;
using UnityEngine.UIElements;
using Starfall.Domain;

namespace Starfall.UI
{
    /// <summary>
    /// Walks a visual tree and translates TextElement text and tooltips from their
    /// English source (UXML attributes or code-built defaults). The English source and
    /// the last value this pass wrote are tracked per element in userData (nothing else
    /// in the project uses userData), so applying again after a language switch is
    /// idempotent and never clobbers text a controller has refreshed with dynamic
    /// snapshot content. Elements carrying the "l10n-skip" class are left alone (e.g.
    /// the language button's own caption).
    /// </summary>
    public static class UiLocalizer
    {
        private const string SkipClass = "l10n-skip";

        private sealed class L10nState
        {
            public string SourceText;
            public string WrittenText;
            public string SourceTooltip;
            public string WrittenTooltip;
        }

        public static void Apply(VisualElement root)
        {
            if (root == null) return;
            root.EnableInClassList("lang-zh", L10n.IsChinese);
            root.Query<VisualElement>().ForEach(TranslateElement);
        }

        private static void TranslateElement(VisualElement element)
        {
            if (element.ClassListContains(SkipClass)) return;
            var state = State(element);

            if (element is TextElement textElement)
                LocalizeValue(textElement.text, value => textElement.text = value,
                    s => s.SourceText, (s, v) => s.SourceText = v,
                    s => s.WrittenText, (s, v) => s.WrittenText = v, state);
            if (!string.IsNullOrEmpty(element.tooltip))
                LocalizeValue(element.tooltip, value => element.tooltip = value,
                    s => s.SourceTooltip, (s, v) => s.SourceTooltip = v,
                    s => s.WrittenTooltip, (s, v) => s.WrittenTooltip = v, state);
        }

        private static void LocalizeValue(
            string current,
            Action<string> setText,
            Func<L10nState, string> getSource,
            Action<L10nState, string> setSource,
            Func<L10nState, string> getWritten,
            Action<L10nState, string> setWritten,
            L10nState state)
        {
            var english = getSource(state);
            if (english == null)
            {
                english = current;
                setSource(state, english);
            }

            var lastWritten = getWritten(state);
            if (L10n.IsChinese)
            {
                // Only translate values that are still the captured English source or
                // our own previous output; anything else was refreshed by a controller.
                if (!string.Equals(current, english, StringComparison.Ordinal) &&
                    !string.Equals(current, lastWritten, StringComparison.Ordinal)) return;
                var translated = NormalizeForDevice(L10n.Tr(english));
                if (!string.Equals(translated, current, StringComparison.Ordinal))
                {
                    setText(translated);
                    setWritten(state, translated);
                }
            }
            else if (lastWritten != null && string.Equals(current, lastWritten, StringComparison.Ordinal))
            {
                setText(NormalizeForDevice(english));
                setWritten(state, null);
            }
        }

        // Touch devices have no keyboard, so "[M]"-style hints are noise.
        private static string NormalizeForDevice(string value)
        {
            if (string.IsNullOrEmpty(value) || !UnityEngine.Application.isMobilePlatform) return value;
            return System.Text.RegularExpressions.Regex.Replace(
                value, @"\s*\[[A-Z0-9]{1,3}\]$", string.Empty);
        }

        private static L10nState State(VisualElement element)
        {
            if (!(element.userData is L10nState state))
            {
                state = new L10nState();
                element.userData = state;
            }
            return state;
        }
    }
}
