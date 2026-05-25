using Content.Server._KS14.AnnouncementWebhook;
using Content.Server._KS14.Antag;
using Content.Server._KS14.IoC;
using Content.Shared._KS14.IoC;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Entry;

internal sealed class KsEntryPoint : GameServer
{
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly LastRolledAntagManager _lastRolledAntagManager = default!;
    [Dependency] private readonly AnnouncementWebhookManager _announcementWebhookManager = default!;
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;

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
    }

    public override void Update(ModUpdateLevel level, FrameEventArgs frameEventArgs)
    {
        base.Update(level, frameEventArgs);

        switch (level)
        {
            case ModUpdateLevel.FramePostEngine:
                _announcementWebhookManager.Update();
                break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _announcementWebhookManager.Shutdown();

        var destinationPath = _configurationManager.GetCVar(CCVars.DestinationFile);
        if (!string.IsNullOrEmpty(destinationPath))
        {
            _lastRolledAntagManager.Shutdown();
        }
    }
}
