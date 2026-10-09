using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace SharpLibm.Tests
{
    /// <summary>
    /// musl libc-test math vectors (src/math/{sanity,special,ucb,crlibm}/*.h):
    /// lines T(rounding, inputs…, outputs…, flags). Only round-to-nearest lines
    /// are used — the CLI fixes the rounding mode — and exception flags are
    /// not checked (the CLI raises none).
    /// </summary>
    internal static class LibcTest
    {
        public sealed class Vector
        {
            public string File;
            public int Line;
            public string[] Fields;   // without the leading rounding mode
            public string Flags => Fields[Fields.Length - 1];
        }

        private static readonly Regex Include = new(@"#include\s+""((?:sanity|special|ucb|crlibm)/[\w]+\.h)""");
        private static readonly Regex Entry = new(@"^\s*T\((.*)\)\s*$");

        /// <summary>Vectors of one function: the headers its driver (src/math/name.c) includes.</summary>
        public static List<Vector> Load(string mathDir, string function)
        {
            var result = new List<Vector>();
            string driver = Path.Combine(mathDir, function + ".c");
            if (!File.Exists(driver)) return result;
            foreach (Match m in Include.Matches(File.ReadAllText(driver)))
            {
                string header = Path.Combine(mathDir, m.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(header)) continue;
                string[] lines = File.ReadAllLines(header);
                for (int i = 0; i < lines.Length; i++)
                {
                    Match t = Entry.Match(lines[i]);
                    if (!t.Success) continue;
                    string[] fields = t.Groups[1].Value.Split(',');
                    for (int k = 0; k < fields.Length; k++) fields[k] = fields[k].Trim();
                    if (fields[0] != "RN") continue;
                    result.Add(new Vector { File = m.Groups[1].Value, Line = i + 1, Fields = fields[1..] });
                }
            }
            return result;
        }

        public static long ParseInt(string s) => s switch
        {
            "FP_ILOGB0" => Libm.FP_ILOGB0,
            "FP_ILOGBNAN" => Libm.FP_ILOGBNAN,
            "-1U/2" => int.MaxValue,
            _ => long.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
