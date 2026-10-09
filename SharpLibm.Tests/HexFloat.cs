using System;
using System.Globalization;

namespace SharpLibm.Tests
{
    /// <summary>C99 hexadecimal floating literals (0x1.8p+1), which .NET does not parse.</summary>
    internal static class HexFloat
    {
        public static double ParseDouble(string s)
        {
            s = s.Trim();
            bool negative = s.StartsWith('-');
            if (negative || s.StartsWith('+')) s = s.Substring(1);
            double value;
            if (s.Equals("inf", StringComparison.OrdinalIgnoreCase) || s.Equals("infinity", StringComparison.OrdinalIgnoreCase))
                value = double.PositiveInfinity;
            else if (s.Equals("nan", StringComparison.OrdinalIgnoreCase))
                value = BitConverter.Int64BitsToDouble(0x7FF8_0000_0000_0000);   // positive quiet NaN; .NET's double.NaN has the sign bit set
            else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = ParseHex(s.Substring(2));
            else
                value = double.Parse(s, CultureInfo.InvariantCulture);
            // Negation through the sign bit: it also gives -nan its sign.
            return negative ? BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) ^ long.MinValue) : value;
        }

        // The literals are exact in float; the NaN keeps its sign through the conversion.
        public static float ParseSingle(string s) => (float)ParseDouble(s);

        // Exact for up to 64 significant bits, which covers double and float literals.
        private static double ParseHex(string s)
        {
            int p = s.IndexOfAny(new[] { 'p', 'P' });
            string mantissa = p < 0 ? s : s.Substring(0, p);
            int exponent = p < 0 ? 0 : int.Parse(s.Substring(p + 1), CultureInfo.InvariantCulture);
            int dot = mantissa.IndexOf('.');
            if (dot >= 0)
            {
                exponent -= 4 * (mantissa.Length - dot - 1);
                mantissa = mantissa.Remove(dot, 1);
            }
            mantissa = mantissa.TrimStart('0');
            if (mantissa.Length == 0) return 0.0;
            if (mantissa.Length > 16) throw new FormatException("hex literal too long: " + s);
            ulong m = ulong.Parse(mantissa, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            // Drop trailing zero bits so the significand fits 53 bits; the
            // literals in the suites are exact doubles, so then both the
            // conversion and the scaling are exact.
            while (m != 0 && (m & 1) == 0) { m >>= 1; exponent++; }
            if (m >= 1UL << 53) throw new FormatException("more than 53 significant bits: " + s);
            return Math.ScaleB((double)m, exponent);
        }
    }
}
