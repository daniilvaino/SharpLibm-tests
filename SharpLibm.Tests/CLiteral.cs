using System;
using System.Globalization;
using System.Numerics;

namespace SharpLibm.Tests
{
    /// <summary>
    /// C floating literals (decimal and hexadecimal, with suffixes) rounded to
    /// binary32 or binary64 exactly as a C compiler does it — to nearest, ties
    /// to even, with subnormals — plus whether the rounding was exact. The
    /// value is returned as the format's bit pattern (in a ulong).
    /// </summary>
    internal static class CLiteral
    {
        /// <param name="bits">32 or 64.</param>
        public static bool TryParse(string s, int bits, out ulong result, out bool exact)
        {
            result = 0; exact = true;
            s = s.Trim();
            bool negative = false;
            while (s.StartsWith('-') || s.StartsWith('+'))
            {
                if (s[0] == '-') negative = !negative;
                s = s.Substring(1).TrimStart();
            }
            int p = bits == 32 ? 24 : 53, emin = bits == 32 ? -126 : -1022, emax = bits == 32 ? 127 : 1023;
            ulong sign = negative ? (bits == 32 ? 0x8000_0000ul : 0x8000_0000_0000_0000ul) : 0;
            ulong inf = bits == 32 ? 0x7f80_0000ul : 0x7ff0_0000_0000_0000ul;
            ulong qnan = bits == 32 ? 0x7fc0_0000ul : 0x7ff8_0000_0000_0000ul;
            ulong snan = bits == 32 ? 0x7fa0_0000ul : 0x7ff4_0000_0000_0000ul;
            switch (s)
            {
                // glibc's gen-libm-test.py: overflowed/underflowed results, here in round-to-nearest.
                case "plus_infty": case "inf": case "INFINITY": case "plus_oflow": result = sign | inf; return true;
                case "plus_uflow": result = sign; return true;
                case "minus_uflow": result = sign ^ (bits == 32 ? 0x8000_0000ul : 0x8000_0000_0000_0000ul); return true;
                case "minus_infty": case "minus_oflow": result = (sign ^ (bits == 32 ? 0x8000_0000ul : 0x8000_0000_0000_0000ul)) | inf; return true;
                case "qnan_value": case "nan": case "NAN": result = sign | qnan; return true;
                case "snan_value": result = sign | snan; return true;
                case "plus_zero": result = sign; return true;
                case "minus_zero": result = sign ^ (bits == 32 ? 0x8000_0000ul : 0x8000_0000_0000_0000ul); return true;
                case "max_value": result = sign | (inf - 1); return true;
                case "min_value": result = sign | (1ul << (p - 1)); return true;
                case "min_subnorm_value": result = sign | 1; return true;
                // glibc's libm-test-driver.c constants (LITM(M_PI) etc.), to more digits than binary64 needs.
                case "lit_pi": s = "3.14159265358979323846264338327950288"; break;
                case "lit_pi_2_d": s = "1.57079632679489661923132169163975144"; break;
                case "lit_pi_4_d": s = "0.785398163397448309615660845819875721"; break;
                case "lit_e": s = "2.71828182845904523536028747135266250"; break;
                case "lit_pi_3_m_4_d": s = "2.356194490192344928846982537459627163"; break;
                case "lit_pi_3_m_4_ln10_m_d": s = "1.023282265381381010614337719073516828"; break;
                case "lit_pi_2_ln10_m_d": s = "0.682188176920920673742891812715677885"; break;
                case "lit_pi_4_ln10_m_d": s = "0.341094088460460336871445906357838943"; break;
                case "lit_pi_ln10_d": s = "1.364376353841841347485783625431355770"; break;
            }
            if (!TryRational(s, out BigInteger num, out BigInteger den) && !TryExpression(s, p, emin, emax, out num, out den)) return false;
            if (num.Sign < 0) { num = -num; sign ^= bits == 32 ? 0x8000_0000ul : 0x8000_0000_0000_0000ul; }
            result = sign | Round(num, den, p, emin, emax, inf, out exact);
            return true;
        }

        // ---- small constant expressions of finite values: "max_value / 2", "2 * min_subnorm_value", MIN_EXP-MANT_DIG ----

        private static bool TryExpression(string s, int p, int emin, int emax, out BigInteger num, out BigInteger den)
        {
            num = 0; den = 1;
            var tokens = new System.Collections.Generic.List<string>();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(s,
                @"0[xX][0-9a-fA-F.]+(?:[pP][-+]?\d+)?[fFlLuU]*|[\d.]+(?:[eE][-+]?\d+)?[fFlLuU]*|[A-Za-z_]\w*|\S"))
                tokens.Add(m.Value);
            int pos = 0;
            try
            {
                (num, den) = Sum(tokens, ref pos, p, emin, emax);
                if (pos != tokens.Count) return false;
                if (den.Sign < 0) { num = -num; den = -den; }
                return !den.IsZero;
            }
            catch (FormatException) { return false; }
        }

        private static (BigInteger, BigInteger) Sum(System.Collections.Generic.List<string> t, ref int p, int prec, int emin, int emax)
        {
            var (n, d) = Product(t, ref p, prec, emin, emax);
            while (p < t.Count && t[p] is "+" or "-")
            {
                bool minus = t[p++] == "-";
                var (n2, d2) = Product(t, ref p, prec, emin, emax);
                n = n * d2 + (minus ? -n2 : n2) * d; d *= d2;
            }
            return (n, d);
        }

        private static (BigInteger, BigInteger) Product(System.Collections.Generic.List<string> t, ref int p, int prec, int emin, int emax)
        {
            var (n, d) = Unit(t, ref p, prec, emin, emax);
            while (p < t.Count && t[p] is "*" or "/")
            {
                bool div = t[p++] == "/";
                var (n2, d2) = Unit(t, ref p, prec, emin, emax);
                if (div) { n *= d2; d *= n2; } else { n *= n2; d *= d2; }
            }
            return (n, d);
        }

        private static (BigInteger, BigInteger) Unit(System.Collections.Generic.List<string> t, ref int p, int prec, int emin, int emax)
        {
            if (p >= t.Count) throw new FormatException();
            string s = t[p++];
            if (s == "-") { var (n, d) = Unit(t, ref p, prec, emin, emax); return (-n, d); }
            if (s == "+") return Unit(t, ref p, prec, emin, emax);
            if (s == "(")
            {
                var v = Sum(t, ref p, prec, emin, emax);
                if (p >= t.Count || t[p++] != ")") throw new FormatException();
                return v;
            }
            switch (s)
            {
                case "max_value": return (((BigInteger.One << prec) - 1) << (emax - prec + 1), 1);
                case "min_value": return (1, BigInteger.One << -emin);
                case "min_subnorm_value": return (1, BigInteger.One << (prec - 1 - emin));
                case "MANT_DIG": return (prec, 1);
                case "MIN_EXP": return (emin + 1, 1);
                case "MAX_EXP": return (emax + 1, 1);
            }
            string lit = s.TrimEnd('u', 'U', 'l', 'L');
            if (lit.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && lit.IndexOfAny(new[] { '.', 'p', 'P' }) < 0)
                return (BigInteger.Parse("0" + lit.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture), 1);
            if (TryRational(s, out BigInteger num, out BigInteger den)) return (num, den);
            throw new FormatException();
        }

        /// <summary>Exact value of a decimal or hex literal as num/den.</summary>
        private static bool TryRational(string s, out BigInteger num, out BigInteger den)
        {
            num = 0; den = 1;
            bool hex = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            // In a hex integer ("0xffffff") f is a digit, not a suffix.
            s = hex && s.IndexOfAny(new[] { 'p', 'P' }) < 0 ? s.TrimEnd('l', 'L', 'u', 'U') : s.TrimEnd('f', 'F', 'l', 'L');
            if (s.Length == 0) return false;
            if (hex) s = s.Substring(2);
            int expAt = s.IndexOfAny(hex ? new[] { 'p', 'P' } : new[] { 'e', 'E' });
            if (hex && expAt < 0 && s.Contains('.')) return false;
            string mant = expAt < 0 ? s : s.Substring(0, expAt);
            int exp = 0;
            if (expAt >= 0 && !int.TryParse(s.Substring(expAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exp)) return false;
            int dot = mant.IndexOf('.');
            int fracDigits = 0;
            if (dot >= 0) { fracDigits = mant.Length - dot - 1; mant = mant.Remove(dot, 1); }
            if (mant.Length == 0) return false;
            foreach (char c in mant)
                if (!(hex ? Uri.IsHexDigit(c) : char.IsAsciiDigit(c))) return false;
            num = BigInteger.Parse((hex ? "0" : "") + mant, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture);
            if (hex)
            {
                int e2 = exp - 4 * fracDigits;
                if (e2 >= 0) num <<= e2; else den <<= -e2;
            }
            else
            {
                int e10 = exp - fracDigits;
                if (e10 >= 0) num *= BigInteger.Pow(10, e10); else den = BigInteger.Pow(10, -e10);
            }
            return true;
        }

        /// <summary>num/den (non-negative) rounded to nearest-even into a p-bit format; returns the bits.</summary>
        private static ulong Round(BigInteger num, BigInteger den, int p, int emin, int emax, ulong inf, out bool exact)
        {
            exact = true;
            if (num.IsZero) return 0;
            int e = (int)(num.GetBitLength() - den.GetBitLength());   // floor(log2(num/den)) is e or e-1
            if (e >= 0 ? num < (den << e) : (num << -e) < den) e--;
            if (e > emax + 1) { exact = false; return inf; }
            int q = Math.Max(e, emin) - (p - 1);                       // exponent of the ulp
            BigInteger n2 = q < 0 ? num << -q : num, d2 = q < 0 ? den : den << q;
            BigInteger m = BigInteger.DivRem(n2, d2, out BigInteger r);
            exact = r.IsZero;
            int c = (r * 2).CompareTo(d2);
            if (c > 0 || (c == 0 && !m.IsEven)) m++;
            if (m.GetBitLength() > p) { m >>= 1; q++; }               // rounded up to the next binade
            if (m.IsZero) return 0;
            int biased = m.GetBitLength() < p ? 0 : q + (p - 1) - emin + 1;   // 0 for subnormals
            if (q + (p - 1) > emax) { exact = false; return inf; }
            ulong frac = (ulong)(m & ((BigInteger.One << (p - 1)) - 1));
            return ((ulong)biased << (p - 1)) | frac;
        }
    }
}
