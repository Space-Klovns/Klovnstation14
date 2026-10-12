using System.Security.Cryptography;
using System.Text;
using Content.Shared._KS14.Localization;
using Robust.Packaging.AssetProcessing;

namespace Content.Packaging._KS14.Localization;

public static class KsPrototypeNameCacheGenerator
{
    public static void Generate(string resourceDirectory)
    {
        var root = Path.GetFullPath(resourceDirectory);
        var sources = new List<AssetFile>();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Changes to compiler/runtime format also require regeneration.
        hash.AppendData(File.ReadAllBytes(typeof(KsPrototypeNameCacheGenerator).Assembly.Location));
        hash.AppendData(File.ReadAllBytes(typeof(KsPrototypeNameCache).Assembly.Location));
        foreach (var directory in new[] { "Prototypes", "Locale", "IgnoredPrototypes", "_KsModule_ReplacedPrototypes" })
        {
            var sourceDirectory = Path.Combine(root, directory);
            if (!Directory.Exists(sourceDirectory))
                continue;
            foreach (var path in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                         .Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!KsPrototypeNameCacheCompiler.IsSource(relative))
                    continue;
                var bytes = File.ReadAllBytes(path);
                hash.AppendData(Encoding.UTF8.GetBytes(relative + "\0"));
                hash.AppendData(bytes);
                sources.Add(new AssetFileMemory(relative, bytes));
            }
        }
        var fingerprint = Convert.ToHexString(hash.GetHashAndReset());
        var output = Path.Combine(root, KsPrototypeNameCache.ResourcePath);
        var hashPath = output + ".hash";
        if (File.Exists(output) && File.Exists(hashPath) && File.ReadAllText(hashPath) == fingerprint)
        {
            Console.WriteLine("Prototype-name cache is up to date.");
            return;
        }
        var cache = KsPrototypeNameCacheCompiler.Compile(sources);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // Client and server can be built in parallel. Publish complete files atomically.
        WriteAtomic(output, cache);
        WriteAtomic(hashPath, Encoding.UTF8.GetBytes(fingerprint));
        Console.WriteLine($"Generated prototype-name cache ({cache.Length:N0} bytes).");
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
