using Content.Shared._KS14.PopupLocalization;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.FieldCommander;

/// <summary>
///     Sent to a field commander defender whose points changed, for the client to show what they got or lost and why.
/// </summary>
/// <remarks>
///     The new total isn't sent: the client reads it off the body, where the shop keeps it too.
/// </remarks>
/// <param name="Delta">What changed. Negative for a loss.</param>
/// <param name="Reason">
///     A short reason, such as what was killed, unformatted: the client formats it in its own language, names in it
///         included.
/// </param>
[Serializable, NetSerializable]
public sealed class KsFieldCommanderPointsChangedEvent(int delta, KsPopupPayload reason) : EntityEventArgs
{
    public readonly int Delta = delta;

    public readonly KsPopupPayload Reason = reason;
}
