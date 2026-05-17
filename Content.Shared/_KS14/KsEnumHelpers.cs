using System.Diagnostics.CodeAnalysis;
using Robust.Shared.Reflection;

namespace Content.Shared._KS14;

public static class KsEnumHelpers
{
    private static readonly Dictionary<string, Enum?> EnumKeyCache = [];

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
