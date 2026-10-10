using Content.Server._KS14.AdminMusic;
using Content.Server._KS14.AnnouncementWebhook;
using Content.Server._KS14.Antag;
using Content.Server._KS14.IoC;
using Content.Server._KS14.Llm;
using Content.Server._KS14.TTS;
using Content.Server._KS14.Voice;
using Content.Shared._KS14.IoC;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Entry;

internal sealed partial class KsEntryPoint : GameServer
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private LastRolledAntagManager _lastRolledAntagManager = default!;
    [Dependency] private AnnouncementWebhookManager _announcementWebhookManager = default!;
    [Dependency] private SystemCollectionHookManager _systemCollectionHookManager = default!;
    [Dependency] private KsAdminMusicManager _adminMusicManager = default!;
    [Dependency] private KsLlmManager _llmManager = default!;
    [Dependency] private KsVoiceLinkManager _voiceLinkManager = default!;
    [Dependency] private KsVoiceUplinkManager _voiceUplinkManager = default!;
    [Dependency] private KsTtsPreviewManager _ttsPreviewManager = default!;

    public override void PreInit()
    {
        KsServerContentIoC.Register(Dependencies);

        base.PreInit();
    }

    public override void Init()
    {
        base.Init();
        Dependencies.BuildGraph();
        Dependencies.InjectDependencies(this);

        _componentFactory.RegisterIgnore(KsIgnoredComponents.List);
    }

    public override void PostInit()
    {
        base.PostInit();

        _lastRolledAntagManager.Initialize();
        _announcementWebhookManager.Initialize();

        _systemCollectionHookManager.TryInit();
        _adminMusicManager.Initialise();
        _llmManager.Initialize();
        _voiceLinkManager.Initialize();
        _voiceUplinkManager.Initialize();
        _ttsPreviewManager.Initialize();
    }

    public override void Update(ModUpdateLevel level, FrameEventArgs frameEventArgs)
    {
        base.Update(level, frameEventArgs);

        switch (level)
        {
            case ModUpdateLevel.PostEngine:
                _announcementWebhookManager.Update();
                _adminMusicManager.Update();
                _llmManager.Update();
                _ttsPreviewManager.Update();
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _announcementWebhookManager.Shutdown();
        _llmManager.Shutdown();
        _voiceUplinkManager.Shutdown();
        _voiceLinkManager.Shutdown();
        _ttsPreviewManager.Shutdown();

        var destinationPath = _configurationManager.GetCVar(CCVars.DestinationFile);
        if (!string.IsNullOrEmpty(destinationPath))
        {
            _lastRolledAntagManager.Shutdown();
        }
    }
}
