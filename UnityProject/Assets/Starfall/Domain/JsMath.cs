using System;

namespace Starfall.Domain
{
    /// <summary>JavaScript number helpers used by the legacy-compatible simulation.</summary>
    public static class JsMath
    {
        private const double TwoPow52 = 4503599627370496d;

        /// <summary>
        /// ECMAScript Math.round semantics: ties go toward positive infinity and
        /// values in [-0.5, 0) produce negative zero.
        /// </summary>
        public static double Round(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value == 0d || Math.Abs(value) >= TwoPow52)
            {
                return value;
            }

            if (value >= -0.5d && value < 0d)
            {
                return -0d;
            }

            return Math.Floor(value + 0.5d);
        }

        public static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static int Imul(int a, int b)
        {
            return unchecked(a * b);
        }
    }
}
