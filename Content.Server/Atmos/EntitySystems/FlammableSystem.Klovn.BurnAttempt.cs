using Content.Shared._KS14.Atmos.Flammable;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class FlammableSystem
{
    /// <summary>
    ///     Whether anything objects to the entity catching fire or staying alight; see <see cref="KsBurnAttemptEvent"/>.
    /// </summary>
    private bool KsCanBurn(EntityUid uid)
    {
        var burnAttemptEvent = new KsBurnAttemptEvent();
        RaiseLocalEvent(uid, ref burnAttemptEvent);
        return !burnAttemptEvent.Cancelled;
    }
}
