using System;
using System.Collections.Generic;

namespace Cheeze.Managed;

public static class IdentitySnapshot
{
    // Duplicate native references indicate ambiguous ownership, rather than two
    // items that can safely be collapsed into one set member.
    public static HashSet<T> Capture<T>(IEnumerable<T> identities) where T : notnull
    {
        var result = new HashSet<T>();
        foreach (var identity in identities)
            if (!result.Add(identity))
                throw new InvalidOperationException("Duplicate item identity in inventory snapshot.");
        return result;
    }
}
