using System.Linq;
using Content.Client._KS14.AdminMusic;
using Content.Client._KS14.IoC;
using Content.Client._KS14.TTS;
using Content.Client._KS14.Voice;
using Content.Shared._KS14.Configuration;
using Content.Shared._KS14.IoC;
using Content.Shared.CCVar;
using Robust.Client;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Entry;

internal sealed partial class KsEntryPoint : GameClient
{
    internal const string ConfigPresetsDir = "/ConfigPresets/";
    private const string ConfigPresetsDirBuild = $"{ConfigPresetsDir}Build/";

    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IResourceManager _resourceManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IBaseClient _baseClient = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private KsAdminMusicManager _adminMusicManager = default!;
    [Dependency] private KsVoiceNetManager _voiceNetManager = default!;
    [Dependency] private KsTtsPreviewManager _ttsPreviewManager = default!;

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
        RegisterIgnoredPrototypes();

        _adminMusicManager.Initialise();
        _voiceNetManager.Initialize();
        _ttsPreviewManager.Initialize();
    }

    /// <summary>
    ///     Server-only fork prototype kinds, which the client would otherwise fail to load.
    /// </summary>
    private void RegisterIgnoredPrototypes()
    {
        _prototypeManager.RegisterIgnore("speczone");
        _prototypeManager.RegisterIgnore("packetFrequency");
        _prototypeManager.RegisterIgnore("ksLlmPersona");
        _prototypeManager.RegisterIgnore("ksLlmTool");
    }

    /// <summary>
    ///     Presets this client has loaded, so the server's list, which arrives on connecting, only adds the rest.
    /// </summary>
    private readonly HashSet<string> _loadedConfigPresets = new();

    private ISawmill _configPresetSawmill = default!;

    // LCDC FUTURE: Remove this if configpresets gets to client on upstream
    /// <summary>
    ///     Loads this client's own presets (and <c>KS14/ks14_base</c>) now, and the server's when it sends its
    ///         <see cref="CCVars.ConfigPresets"/>, which is replicated. Only the cvars the client registers are set:
    ///         a preset is mostly server cvars, but may set a client one too, such as <c>audio.default_concurrent</c>.
    /// </summary>
    private void LoadConfigPresets()
    {
        _configPresetSawmill = _logManager.GetSawmill("configpreset");

        var presets = _configurationManager.GetCVar(CCVars.ConfigPresets).Split(',').ToList();
        presets.Add("KS14/ks14_base");
        LoadConfigPresets(presets);

        // The entry point lives as long as the process, so this never needs unsubscribing.
        _configurationManager.OnValueChanged(CCVars.ConfigPresets, value => LoadConfigPresets(value.Split(',')));
    }

    private void LoadConfigPresets(IEnumerable<string> presets)
    {
        foreach (var rawPreset in presets)
        {
            var preset = rawPreset.Trim();
            if (preset.Length == 0 || !_loadedConfigPresets.Add(preset))
                continue;

            var path = $"{ConfigPresetsDir}{preset}.toml";
            if (!_resourceManager.TryContentFileRead(path, out var file))
            {
                _configPresetSawmill.Error("Unable to load config preset {Preset}!", path);
                continue;
            }

            KsConfigPresets.LoadDefaults(_configurationManager, file, _configPresetSawmill);
            _configPresetSawmill.Info("Loaded config preset: {Preset}", path);
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
        _ttsPreviewManager.Shutdown();

        base.Shutdown();
    }
}
