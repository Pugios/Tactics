using System.Collections.Generic;
using UnityEngine;

namespace Tactics.Settings
{
    /// <summary>
    /// Turns the display's mode list into resolution choices. Unity reports one
    /// entry per size AND refresh rate, so the same size shows up several times;
    /// the menu offers each size once, largest first.
    /// </summary>
    public static class ResolutionOptions
    {
        /// <param name="reported">Sizes the display reports (duplicates allowed).</param>
        /// <param name="alwaysInclude">A size that must be offered even if unreported — the current one, so the menu can show it.</param>
        public static List<Vector2Int> Build(IEnumerable<Vector2Int> reported, Vector2Int alwaysInclude)
        {
            var unique = new HashSet<Vector2Int>();
            foreach (Vector2Int size in reported)
            {
                if (size.x > 0 && size.y > 0) unique.Add(size);
            }
            if (alwaysInclude.x > 0 && alwaysInclude.y > 0) unique.Add(alwaysInclude);

            var sorted = new List<Vector2Int>(unique);
            sorted.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return sorted;
        }

        public static string Label(Vector2Int size) => $"{size.x} x {size.y}";
    }
}
