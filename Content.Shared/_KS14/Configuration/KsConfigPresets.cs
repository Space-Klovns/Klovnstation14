using System.Diagnostics.CodeAnalysis;
using System.IO;
using Nett;
using Robust.Shared.Configuration;

namespace Content.Shared._KS14.Configuration;

/// <summary>
///     Loads a config preset's cvars as defaults, as <see cref="IConfigurationManager.LoadDefaultsFromTomlStream"/> does,
///         except that a cvar this side doesn't register is skipped quietly instead of logged as an error. Client and
///         server load the same presets, and one preset may set <see cref="CVar.CLIENTONLY"/> and
///         <see cref="CVar.SERVERONLY"/> cvars side by side (<c>audio.default_concurrent</c> beside the server's). A
///         cvar registered on neither side, a typo, is caught by <c>KsConfigPresetTest</c> instead.
/// </summary>
public static class KsConfigPresets
{
    /// <summary>
    ///     Every cvar a preset sets, by its full dotted name (<c>audio.default_concurrent</c>), with the value as TOML
    ///         gives it: <see cref="long"/>, <see cref="double"/>, <see cref="bool"/> or <see cref="string"/>.
    /// </summary>
    public static List<(string Name, object Value)> ReadCVars(Stream stream)
    {
        // Toml.ReadString, as Toml.ReadStream isn't allowed in the client's sandbox.
        using var reader = new StreamReader(stream);
        var cvars = new List<(string Name, object Value)>();
        AddTable(Toml.ReadString(reader.ReadToEnd()), string.Empty, cvars);
        return cvars;
    }

    private static void AddTable(TomlTable table, string prefix, List<(string Name, object Value)> cvars)
    {
        foreach (var (key, tomlObject) in table)
        {
            var name = prefix + key;
            switch (tomlObject)
            {
                case TomlTable subTable:
                    AddTable(subTable, name + ".", cvars);
                    break;
                case TomlValue value:
                    cvars.Add((name, value.UntypedValue));
                    break;
            }
        }
    }

    /// <summary>
    ///     Sets every cvar in the preset that this side registers as that cvar's default, so a value a player saved
    ///         still wins over it. Returns how many were set.
    /// </summary>
    public static int LoadDefaults(IConfigurationManager configurationManager, Stream stream, ISawmill sawmill)
    {
        var loaded = 0;
        foreach (var (name, value) in ReadCVars(stream))
        {
            if (!configurationManager.IsCVarRegistered(name))
            {
                sawmill.Debug($"Skipped cvar '{name}': not registered on this side.");
                continue;
            }

            var type = configurationManager.GetCVarType(name);
            if (!TryConvert(value, type, out var converted))
            {
                sawmill.Error($"Config preset value for cvar '{name}' ({value.GetType().Name}) doesn't fit its type, {type.Name}.");
                continue;
            }

            configurationManager.OverrideDefault(name, converted);
            loaded++;
        }

        return loaded;
    }

    // By hand, as Convert.ChangeType isn't allowed in the client's sandbox.
    private static bool TryConvert(object value, Type type, [NotNullWhen(true)] out object? converted)
    {
        converted = null;

        if (type.IsEnum)
        {
            if (Enum.TryParse(type, value.ToString(), ignoreCase: false, out var enumValue))
                converted = enumValue;
            return converted != null;
        }

        converted = value switch
        {
            string text when type == typeof(string) => text,
            bool flag when type == typeof(bool) => flag,
            long number when type == typeof(int) && number is >= int.MinValue and <= int.MaxValue => (int)number,
            long number when type == typeof(long) => number,
            long number when type == typeof(float) => (float)number,
            long number when type == typeof(double) => (double)number,
            double number when type == typeof(float) => (float)number,
            double number when type == typeof(double) => number,
            _ => null,
        };

        return converted != null;
    }
}
