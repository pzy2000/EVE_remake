using System.Collections.Generic;

namespace Starfall.Domain
{
    /// <summary>
    /// Chinese translation tables. English text is the key in every entry; entries are
    /// merged into a single lookup so display code only ever needs L10n.Tr/TrName.
    /// Split into partial files by source: Ui (UXML + UI controllers), Game (simulation
    /// and app log text), Content (catalog data + proper-noun transliteration).
    /// </summary>
    internal static partial class L10nData
    {
        public static Dictionary<string, string> BuildUiTable()
        {
            var table = new Dictionary<string, string>(512, System.StringComparer.Ordinal);
            AddEntries(table, UiEntries);
            AddEntries(table, GameEntries);
            AddEntries(table, ContentEntries);
            AddEntries(table, LegacyEntries);
            return table;
        }

        public static Dictionary<string, string> BuildNameExactTable()
        {
            var table = new Dictionary<string, string>(System.StringComparer.Ordinal);
            AddEntries(table, NameExactEntries);
            return table;
        }

        public static Dictionary<string, string> BuildNameFragmentTable()
        {
            var table = new Dictionary<string, string>(System.StringComparer.Ordinal);
            AddEntries(table, NameFragmentEntries);
            return table;
        }

        public static HashSet<string> BuildMergeSuffixWords()
        {
            return new HashSet<string>(MergeSuffixWords, System.StringComparer.Ordinal);
        }

        public static string[] BuildSyllableVocabulary()
        {
            var vocabulary = new string[SyllableVocabulary.Length];
            for (var i = 0; i < SyllableVocabulary.Length; i++) vocabulary[i] = SyllableVocabulary[i];
            // Longest-first so greedy decomposition prefers whole syllables.
            System.Array.Sort(vocabulary, (left, right) => right.Length.CompareTo(left.Length));
            return vocabulary;
        }

        private static void AddEntries(Dictionary<string, string> table, (string en, string zh)[] entries)
        {
            for (var i = 0; i < entries.Length; i++) table[entries[i].en] = entries[i].zh;
        }
    }
}
