using Content.Packaging;
using Robust.Packaging;
using Content.Packaging._KS14.Localization; // KS14

// KS14 start: build-time cache generation does not package, clear bin, or launch the game
if (args is ["--prototype-name-cache", var resourceDirectory])
{
    KsPrototypeNameCacheGenerator.Generate(resourceDirectory);
    return;
}
// KS14 end

IPackageLogger logger = new PackageLoggerConsole();

if (!CommandLineArgs.TryParse(args, out var parsed))
{
    logger.Error("Unable to parse args, aborting.");
    return;
}

if (parsed.WipeRelease)
    WipeRelease();
else
{
    // Ensure the release directory exists. Otherwise, the packaging will fail.
    Directory.CreateDirectory("release");
}

if (!parsed.SkipBuild)
    WipeBin();

if (parsed.Client)
{
    await ClientPackaging.PackageClient(parsed.SkipBuild, parsed.LogBuild, parsed.Configuration, logger);
}
else
{
    await ServerPackaging.PackageServer(parsed.SkipBuild, parsed.HybridAcz, parsed.LogBuild, logger, parsed.Configuration, parsed.Platforms);
}

void WipeBin()
{
    logger.Info("Clearing old build artifacts (if any)...");

    if (Directory.Exists("bin"))
        Directory.Delete("bin", recursive: true);
}

void WipeRelease()
{
    if (Directory.Exists("release"))
    {
        logger.Info("Cleaning old release packages (release/)...");
        Directory.Delete("release", recursive: true);
    }

    Directory.CreateDirectory("release");
}
