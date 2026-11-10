using System.Collections.Frozen;
using System.IO;
using System.Text;

namespace Content.Shared._KS14.Localization;

/// <summary>
/// Portable name and description tables compiled into the server/client resource packages.
/// </summary>
public static class KsPrototypeNameCache
{
    public const string ResourcePath = "Localization/_KS14/prototype-names.bin";
    public const int Magic = 0x4B534C4E;
    public const int Version = 2;

    public static Dictionary<string, KsPrototypeLocalizationTable> Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
            throw new InvalidDataException("Unsupported packaged prototype-name cache.");

        var cultures = new Dictionary<string, KsPrototypeLocalizationTable>(StringComparer.Ordinal);
        var cultureCount = reader.ReadInt32();
        for (var cultureIndex = 0; cultureIndex < cultureCount; cultureIndex++)
        {
            var culture = reader.ReadString();
            var nameCount = reader.ReadInt32();
            var names = new Dictionary<string, string>(nameCount, StringComparer.Ordinal);
            var descriptions = new Dictionary<string, string>(nameCount, StringComparer.Ordinal);
            for (var nameIndex = 0; nameIndex < nameCount; nameIndex++)
            {
                var id = reader.ReadString();
                names.Add(id, reader.ReadString());
                descriptions.Add(id, reader.ReadString());
            }
            cultures.Add(culture, new KsPrototypeLocalizationTable(names.ToFrozenDictionary(StringComparer.Ordinal),
                descriptions.ToFrozenDictionary(StringComparer.Ordinal)));
        }
        return cultures;
    }

}

public sealed class KsPrototypeLocalizationTable(
    FrozenDictionary<string, string> names, FrozenDictionary<string, string> descriptions)
{
    public FrozenDictionary<string, string> Names { get; } = names;
    public FrozenDictionary<string, string> Descriptions { get; } = descriptions;
}
