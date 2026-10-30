

namespace Content.Shared.Strip;

// KS14 file

public abstract partial class SharedStrippableSystem : EntitySystem
{
    private void KsRaiseStartStripEvent(EntityUid uid, EntityUid userUid)
    {
        var ev = new KsStrippingStartedEvent(userUid);
        RaiseLocalEvent(uid, ref ev);
    }
}

/// <summary>
///     Raised on something after it starts getting stripped.
/// </summary>
/// <param name="userUid">The person doing the stealing.</param>
[ByRefEvent]
public record struct KsStrippingStartedEvent(EntityUid UserUid);
