using Content.Client._KS14.IoC;
using Content.Shared._KS14.IoC;
using Robust.Client;
using Robust.Shared.ContentPack;

namespace Content.Client._KS14.Entry;

internal sealed class KsEntryPoint : GameClient
{
    [Dependency] private readonly IBaseClient _baseClient = default!;
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;

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
    }

    public override void PostInit()
    {
        base.Init();
        _baseClient.PlayerJoinedServer += (_, _) => _systemCollectionHookManager.TryInit();
    }
}
