// Rounding and number formatting exactly as in the model's training data (Python's round(x, n) and f"{x:.nf}"), so
// the scene text and the tool effects match it byte for byte. Python rounds the exact binary value of x, ties to even;
// .NET's Math.Round(x, n) scales by 10^n first, so 0.45 (binary 0.45000000000000001...) became 0.4 where the training
// scenes say 0.5, and -0.04 became 0.0 where they say -0.0.
using System;
using System.Globalization;
using System.Numerics;

namespace SceneAgent
{
    public static class SceneMath
    {
        /// <summary>Python round(x, n) for n >= 0: exact, ties to even, sign kept (round(-0.04, 1) == -0.0).</summary>
        public static double Round(double x, int n)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return x;
            long bits = BitConverter.DoubleToInt64Bits(x);
            bool neg = bits < 0;
            int e = (int)((bits >> 52) & 0x7FF);
            long m = bits & 0xFFFFFFFFFFFFFL;
            if (e == 0) e = 1; else m |= 1L << 52;
            e -= 1075;  // |x| = m * 2^e exactly
            BigInteger num = new BigInteger(m) * BigInteger.Pow(10, n), den = BigInteger.One;
            if (e > 0) num <<= e; else den <<= -e;
            BigInteger q = BigInteger.DivRem(num, den, out BigInteger r);
            int c = (r * 2).CompareTo(den);
            if (c > 0 || (c == 0 && !q.IsEven)) q += 1;
            double v = (double)q / Math.Pow(10, n);  // q and 10^n are exact doubles: the division is correctly rounded
            return neg ? -v : v;
        }

        /// <summary>Python f"{x:.{n}f}".</summary>
        public static string Fixed(double x, int n) => Round(x, n).ToString("F" + n, CultureInfo.InvariantCulture);
    }
}
