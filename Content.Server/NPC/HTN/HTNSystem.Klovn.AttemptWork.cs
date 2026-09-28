using Content.Shared._KS14.IoC;
using Content.Server._KS14.NPC;

namespace Content.Server.NPC.HTN;

public sealed partial class HTNSystem
{
    [Dependency] private SystemCollectionHookManager _collectionHook = default!;

    public bool AttemptWork(EntityUid uid)
    {
        var ev = new AttemptNpcWorkEvent();
        RaiseLocalEvent(uid, ref ev);

        return !ev.Cancelled;
    }
}
