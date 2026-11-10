using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Reflection;

namespace Content.Shared._KS14;

public static class KsEnumHelpers
{
    // Concurrent: test pairs run several clients and servers in one process, each on its own thread. What it caches
    //     is the same for all of them, since it only depends on the loaded types.
    private static readonly ConcurrentDictionary<string, Enum?> EnumKeyCache = new();

    public static Enum? ParseKey(string keyString, [NotNullWhen(true)] out bool isEnum, IReflectionManager reflectionManager)
    {
        if (EnumKeyCache.TryGetValue(keyString, out var foundEnum))
        {
            isEnum = foundEnum is { };
            return foundEnum;
        }

        if (reflectionManager.TryParseEnumReference(keyString, out var @enum))
        {
            EnumKeyCache[keyString] = @enum;
            isEnum = true;
            return @enum;
        }

        EnumKeyCache[keyString] = null;
        isEnum = false;
        return null;
    }
}
