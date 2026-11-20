using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Resist;

/// <summary>
///     tgstation's resist button. The HUD button sends <see cref="KsResistRequestEvent"/>; this raises
///         <see cref="KsResistAttemptEvent"/> on whoever the player controls, and whatever holds them (a grab, a carry)
///         handles it. Add a new kind of restraint by handling that event, not here.
/// </summary>
public sealed partial class KsResistSystem : EntitySystem
{
    [EventSubscription]
    private void OnResistRequest(KsResistRequestEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is { } player)
            TryResist(player);
    }

    /// <summary>
    ///     Tries to break free of whatever holds <paramref name="uid"/>.
    /// </summary>
    /// <returns>Whether anything took the attempt.</returns>
    public bool TryResist(EntityUid uid)
    {
        var attempt = new KsResistAttemptEvent();
        RaiseLocalEvent(uid, ref attempt);
        return attempt.Handled;
    }
}

/// <summary>
///     Sent by a client pressing the resist button.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsResistRequestEvent : EntityEventArgs;

/// <summary>
///     Raised on someone trying to break free. The first restraint to handle it takes the attempt.
/// </summary>
[ByRefEvent]
public record struct KsResistAttemptEvent
{
    public bool Handled;
}
