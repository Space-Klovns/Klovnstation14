using Content.Shared._KS14.IoC;
using Robust.Shared.ContentPack;

namespace Content.Shared._KS14.Entry;

public sealed class KsEntryPoint : GameShared
{
    [Dependency] private readonly SystemCollectionHookManager _systemCollectionHookManager = default!;

    public override void PreInit()
    {
        base.PreInit();
        KsSharedContentIoC.Register(Dependencies);
    }

    public override void Init()
    {
        base.Init();
        Dependencies.BuildGraph();
        Dependencies.InjectDependencies(this);
    }

    public override void Shutdown()
    {
        _systemCollectionHookManager.Reset();
        base.Shutdown();
    }
}
