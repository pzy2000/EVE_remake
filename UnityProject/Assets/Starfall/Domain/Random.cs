using System;
using System.Collections.Generic;

namespace Starfall.Domain
{
    public interface IRandomSource
    {
        uint State { get; }
        double NextDouble();
        double Range(double minimum, double maximum);
        int RangeInclusive(int minimum, int maximum);
        bool Chance(double probability);
    }

    /// <summary>
    /// Bit-for-bit port of js/core/rng.js. State is serializable and all
    /// arithmetic is intentionally unchecked 32-bit arithmetic.
    /// </summary>
    public sealed class Mulberry32 : IRandomSource
    {
        private uint _state;

        public Mulberry32(uint seed)
        {
            _state = seed;
        }

        public uint State => _state;

        public double NextDouble()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                var t = (_state ^ (_state >> 15)) * (1u | _state);
                t = (t + ((t ^ (t >> 7)) * (61u | t))) ^ t;
                return (t ^ (t >> 14)) / 4294967296d;
            }
        }

        public double Range(double minimum, double maximum)
        {
            return minimum + NextDouble() * (maximum - minimum);
        }

        public int RangeInclusive(int minimum, int maximum)
        {
            if (maximum < minimum)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "Maximum must be at least minimum.");
            }

            return minimum + (int)Math.Floor(NextDouble() * ((double)maximum - minimum + 1d));
        }

        public bool Chance(double probability) => NextDouble() < probability;

        public T Pick<T>(IReadOnlyList<T> values)
        {
            if (values == null || values.Count == 0)
            {
                throw new ArgumentException("Cannot pick from an empty collection.", nameof(values));
            }

            return values[(int)Math.Floor(NextDouble() * values.Count)];
        }

        public List<T> Shuffle<T>(IReadOnlyList<T> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var copy = new List<T>(values);
            for (var i = copy.Count - 1; i > 0; i--)
            {
                var j = (int)Math.Floor(NextDouble() * (i + 1));
                var value = copy[i];
                copy[i] = copy[j];
                copy[j] = value;
            }

            return copy;
        }
    }

    public static class Fnv1a
    {
        /// <summary>
        /// Legacy hashString compatibility. Iteration is over UTF-16 code units,
        /// exactly like JavaScript String.charCodeAt.
        /// </summary>
        public static uint HashString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            unchecked
            {
                var hash = 2166136261u;
                for (var i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }

                return hash;
            }
        }
    }
}
