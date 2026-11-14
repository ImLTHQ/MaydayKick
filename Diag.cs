using System.Collections.Generic;

namespace MaydayKick
{
    internal static class Diag
    {
        private static readonly HashSet<string> seen = new HashSet<string>();

        public static bool Once(string key)
        {
            lock (seen) return seen.Add(key);
        }
    }
}
