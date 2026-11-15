using Content.Shared._KS14.RCD;
using Content.Shared.RCD.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.RCD.Systems;

public sealed partial class RCDSystem
{
    /// <summary>
    ///     An instant RCD skips the wait for everything it builds, and for taking apart what its user built with one.
    /// </summary>
    private void KsApplyInstant(RCDComponent component, RCDPrototype prototype, EntityUid? targetUid, EntityUid userUid,
        ref float delay, ref EntProtoId? effectPrototype)
    {
        if (!component.KsInstant ||
            prototype.Mode == RcdMode.Deconstruct && !IsOwnWork(targetUid, userUid))
            return;

        delay = _instantConstructionDelay;
        effectPrototype = _instantConstructionFx;
    }

    /// <summary>
    ///     Whether <paramref name="targetUid"/> was built by <paramref name="userUid"/> with an instant RCD.
    /// </summary>
    private bool IsOwnWork(EntityUid? targetUid, EntityUid userUid)
    {
        return TryComp<KsRcdBuiltComponent>(targetUid, out var builtComponent) && builtComponent.Builder == userUid;
    }

    /// <summary>
    ///     Remembers who built <paramref name="builtUid"/>, when it was built with an instant RCD.
    /// </summary>
    private void KsMarkBuilt(RCDComponent component, EntityUid builtUid, EntityUid userUid)
    {
        if (!component.KsInstant)
            return;

        EnsureComp<KsRcdBuiltComponent>(builtUid).Builder = userUid;
    }
}
