using Content.Client._KS14.AdminMusic;
using Content.Client._KS14.IoC;
using Content.Shared._KS14.IoC;
using Robust.Client;
using Robust.Shared.ContentPack;

namespace Content.Client._KS14.Entry;

internal sealed partial class KsEntryPoint : GameClient
{
    [Dependency] private IBaseClient _baseClient = default!;
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

        _adminMusicManager.Initialise();
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
