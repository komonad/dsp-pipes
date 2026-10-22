using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FluidPipes
{
    internal sealed class ItemAdmission
    {
        private readonly HashSet<int> allowed;

        internal ItemAdmission(
            IEnumerable<int> defaults,
            IEnumerable<int> additions,
            IEnumerable<int> exclusions
        )
        {
            allowed = new HashSet<int>(defaults);
            allowed.UnionWith(additions);
            allowed.ExceptWith(exclusions);
        }

        internal bool Allows(int itemId) => itemId > 0 && allowed.Contains(itemId);

        internal static int[] ParseIds(string value)
        {
            return (value ?? "")
                .Split(
                    new[] { ',', ';', ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
                .Select(token =>
                {
                    if (
                        !int.TryParse(
                            token,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int itemId
                        )
                        || itemId <= 0
                    )
                    {
                        throw new FormatException("Invalid item ID: " + token);
                    }

                    return itemId;
                })
                .Distinct()
                .ToArray();
        }
    }
}
