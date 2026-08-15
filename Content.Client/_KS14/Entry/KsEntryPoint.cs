using System.Linq;
using Content.Client._KS14.AdminMusic;
using Content.Client._KS14.IoC;
using Content.Shared._KS14.IoC;
using Content.Shared.CCVar;
using Robust.Client;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;

namespace Content.Client._KS14.Entry;

internal sealed partial class KsEntryPoint : GameClient
{
    internal const string ConfigPresetsDir = "/ConfigPresets/";
    private const string ConfigPresetsDirBuild = $"{ConfigPresetsDir}Build/";

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IResourceManager _resourceManager = default!;
    [Dependency] private IBaseClient _baseClient = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private KsAdminMusicManager _adminMusicManager = default!;

    public override void PreInit()
    {
        base.PreInit();
        KsClientContentIoC.Register(Dependencies);
    }

    public override void Init()
    {
        base.Init();
        Dependencies.BuildGraph();
        Dependencies.InjectDependencies(this);

        LoadConfigPresets();

        _adminMusicManager.Initialise();
    }

    // LCDC FUTURE: Remove this if configpresets gets to client on upstream
    private void LoadConfigPresets()
    {
        var sawmill = _logManager.GetSawmill("configpreset");
        var presets = _configurationManager.GetCVar(CCVars.ConfigPresets).Split(',').ToList();
        presets.Add("KS14/ks14_base");

        foreach (var preset in presets)
        {
            if (preset.IsWhiteSpace())
                continue;

            var path = $"{ConfigPresetsDir}{preset}.toml";
            if (!_resourceManager.TryContentFileRead(path, out var file))
            {
                sawmill.Error("Unable to load config preset {Preset}!", path);
                continue;
            }

            _configurationManager.LoadDefaultsFromTomlStream(file);
            sawmill.Info("Loaded config preset: {Preset}", path);
        }
    }

    public override void PostInit()
    {
        base.Init();
        _baseClient.PlayerJoinedServer += (_, _) => _systemCollectionHookManager.TryInit();
    }

    public override void Shutdown()
    {
        _adminMusicManager.Shutdown();

        base.Shutdown();
    }
}
