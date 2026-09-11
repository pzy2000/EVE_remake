using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Starfall.Domain
{
    public enum L10nLanguage
    {
        English = 0,
        Chinese = 1,
    }

    /// <summary>
    /// Lightweight gettext-style localization. English text is the key; the Chinese
    /// dictionary maps it to a translation. Unknown keys pass through unchanged so a
    /// missing entry can never break the game. All persisted state stores English
    /// canonical strings; translation happens only at display/emission time.
    /// The library-level default is English so deterministic simulation tests that
    /// assert English strings stay valid; the application root applies the persisted
    /// player preference (default Chinese) at startup.
    /// </summary>
    public static class L10n
    {
        private static readonly object Gate = new object();
        private static L10nLanguage language = L10nLanguage.English;
        private static Dictionary<string, string> zhTable;
        private static Dictionary<string, string> zhNameExact;
        private static Dictionary<string, string> zhNameFragments;
        private static HashSet<string> zhMergeSuffixWords;

        public static event Action LanguageChanged;

        public static L10nLanguage Language
        {
            get { lock (Gate) { return language; } }
        }

        public static bool IsChinese => Language == L10nLanguage.Chinese;

        public static void SetLanguage(L10nLanguage value)
        {
            bool changed;
            lock (Gate)
            {
                changed = language != value;
                if (changed) language = value;
            }
            if (changed) LanguageChanged?.Invoke();
        }

        /// <summary>Translates an English key. English mode or a dictionary miss returns the key itself.</summary>
        public static string Tr(string key)
        {
            if (string.IsNullOrEmpty(key) || !IsChinese) return key;
            return Table.TryGetValue(key, out var zh) ? zh : key;
        }

        /// <summary>Translates a format key such as "Docked at {0}." and fills the placeholders.</summary>
        public static string Tr(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) return key;
            var template = IsChinese && Table.TryGetValue(key, out var zh) ? zh : key;
            return args == null || args.Length == 0
                ? template
                : string.Format(CultureInfo.InvariantCulture, template, args);
        }

        /// <summary>
        /// Transliterates generated proper nouns (systems, planets, moons, belts,
        /// stations, gates, ship names) built from the closed UniverseGenerator
        /// vocabulary. Exact matches win first, then structural patterns, then
        /// token-by-token transliteration; unknown tokens pass through unchanged
        /// (faction abbreviations, roman numerals, numbers, pilot callsigns).
        /// </summary>
        public static string TrName(string name)
        {
            if (string.IsNullOrEmpty(name) || !IsChinese) return name;
            if (NameExact.TryGetValue(name, out var exact)) return exact;
            foreach (var rule in NamePatternRules)
            {
                var applied = rule(name);
                if (applied != null) return applied;
            }
            return TransliterateTokens(name, " ", MergeSuffixWords);
        }

        /// <summary>Transliterates person names ("Aren Voss" -> "阿伦·沃斯") joining parts with a middle dot.</summary>
        public static string TrPersonName(string name)
        {
            if (string.IsNullOrEmpty(name) || !IsChinese) return name;
            if (NameExact.TryGetValue(name, out var exact)) return exact;
            return TransliterateTokens(name, "\u00B7", null);
        }

        private static string TransliterateTokens(string name, string separator, HashSet<string> mergeSuffixWords)
        {
            var tokens = name.Split(' ');
            if (tokens.Length == 1) return LookupFragment(tokens[0]);
            var sb = new StringBuilder();
            for (var i = 0; i < tokens.Length; i++)
            {
                if (i > 0 && (mergeSuffixWords == null || !mergeSuffixWords.Contains(tokens[i]))) sb.Append(separator);
                sb.Append(LookupFragment(tokens[i]));
            }
            return sb.ToString();
        }

        private static string LookupFragment(string token)
        {
            if (NameFragments.TryGetValue(token, out var zh)) return zh;
            // Ship names live in the main content table; NPC names are composed of
            // faction abbreviations and ship names ("Elite AUR Shrike").
            if (Table.TryGetValue(token, out var content)) return content;
            return DecomposeSyllables(token);
        }

        private static string[] syllableVocabulary;

        /// <summary>
        /// UniverseGenerator concatenates syllables with no separator ("Hel" + "ara" =
        /// "Helara"), so an unknown token may still decompose into known syllables.
        /// Greedy longest-first match; any residue leaves the token untouched (faction
        /// abbreviations, roman numerals, player callsigns stay in Latin script).
        /// </summary>
        private static string DecomposeSyllables(string token)
        {
            var vocabulary = syllableVocabulary;
            if (vocabulary == null)
            {
                lock (Gate)
                {
                    if (syllableVocabulary == null) syllableVocabulary = L10nData.BuildSyllableVocabulary();
                }
                vocabulary = syllableVocabulary;
            }

            if (token.Length < 3) return token;
            var builder = new StringBuilder();
            var index = 0;
            while (index < token.Length)
            {
                var matched = false;
                for (var i = 0; i < vocabulary.Length; i++)
                {
                    var syllable = vocabulary[i];
                    if (index + syllable.Length > token.Length ||
                        string.CompareOrdinal(token, index, syllable, 0, syllable.Length) != 0) continue;
                    builder.Append(NameFragments.TryGetValue(syllable, out var zh) ? zh : syllable);
                    index += syllable.Length;
                    matched = true;
                    break;
                }
                if (!matched) return token;
            }
            return builder.ToString();
        }

        private static Dictionary<string, string> Table
        {
            get
            {
                lock (Gate)
                {
                    if (zhTable == null) zhTable = L10nData.BuildUiTable();
                    return zhTable;
                }
            }
        }

        private static Dictionary<string, string> NameExact
        {
            get
            {
                lock (Gate)
                {
                    if (zhNameExact == null) zhNameExact = L10nData.BuildNameExactTable();
                    return zhNameExact;
                }
            }
        }

        private static Dictionary<string, string> NameFragments
        {
            get
            {
                lock (Gate)
                {
                    if (zhNameFragments == null) zhNameFragments = L10nData.BuildNameFragmentTable();
                    return zhNameFragments;
                }
            }
        }

        private static HashSet<string> MergeSuffixWords
        {
            get
            {
                lock (Gate)
                {
                    if (zhMergeSuffixWords == null) zhMergeSuffixWords = L10nData.BuildMergeSuffixWords();
                    return zhMergeSuffixWords;
                }
            }
        }

        private static readonly Func<string, string>[] NamePatternRules =
        {
            name => MatchPrefix(name, "Stargate to ", rest => "通往" + TrName(rest) + "的星门"),
            name => MatchSuffix(name, " Command", head => TrName(head) + "司令部"),
            name => MatchInfix(name, "Station ", (head, tail) => head + " " + tail + "号空间站", true),
            name => MatchInfix(name, "Belt ", (head, tail) => head + "小行星带 " + tail, false),
            name => MatchInfix(name, "- Moon ", (head, tail) => head + " 卫星" + tail, true),
            name => MatchSuffix(name, " Star", head => TrName(head) + "恒星"),
        };

        private static string MatchPrefix(string name, string prefix, Func<string, string> build)
        {
            return name.StartsWith(prefix, StringComparison.Ordinal) ? build(name.Substring(prefix.Length)) : null;
        }

        private static string MatchSuffix(string name, string suffix, Func<string, string> build)
        {
            return name.EndsWith(suffix, StringComparison.Ordinal) ? build(name.Substring(0, name.Length - suffix.Length)) : null;
        }

        private static string MatchInfix(string name, string infix, Func<string, string, string> build, bool numericTail)
        {
            var index = name.IndexOf(infix, StringComparison.Ordinal);
            if (index <= 0) return null;
            var head = name.Substring(0, index).TrimEnd();
            var tail = name.Substring(index + infix.Length);
            if (numericTail && !IsOrdinal(tail)) return null;
            return build(TrName(head), LookupFragment(tail));
        }

        private static bool IsOrdinal(string value)
        {
            if (value.Length == 0) return false;
            for (var i = 0; i < value.Length; i++)
                if (value[i] < '0' || value[i] > '9') return false;
            return true;
        }
    }
}
