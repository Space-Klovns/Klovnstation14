using Content.Shared._KS14.Carrying;
using Content.Shared._KS14.Grab;
using Content.Shared.Cuffs;
using Content.Shared.Cuffs.Components;

namespace Content.Shared._KS14.Resist;

/// <summary>
///     Resisting while cuffed starts breaking out of the cuffs, as clicking the cuffed alert does. Whoever holds or
///         carries them is resisted first, in tgstation's order: someone cuffed and grabbed breaks the grip, then
///         the cuffs.
/// </summary>
public sealed partial class KsCuffResistSystem : EntitySystem
{
    [Dependency] private SharedCuffableSystem _cuffableSystem = default!;

    [SubscribeLocalEvent(after: [typeof(KsGrabSystem), typeof(KsFiremanCarrySystem)])]
    private void OnResistAttempt(Entity<CuffableComponent> cuffed, ref KsResistAttemptEvent args)
    {
        if (args.Handled || cuffed.Comp.CuffedHandCount == 0)
            return;

        args.Handled = true;
        _cuffableSystem.TryUncuff(cuffed.AsNullable(), cuffed.Owner);
    }
}
