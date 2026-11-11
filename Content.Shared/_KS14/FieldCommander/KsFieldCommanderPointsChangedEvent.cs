using Robust.Shared.Serialization;

namespace Content.Shared._KS14.FieldCommander;

/// <summary>
///     Sent to a field commander defender whose points changed, for the client to show what they got or lost and why.
/// </summary>
/// <param name="Delta">What changed. Negative for a loss.</param>
/// <param name="Total">The points they have now.</param>
/// <param name="Reason">Localisation id of a short reason, such as what was killed. Takes <c>name</c>.</param>
/// <param name="ReasonName">The name inserted into <paramref name="Reason"/>, if any.</param>
[Serializable, NetSerializable]
public sealed class KsFieldCommanderPointsChangedEvent(int delta, int total, string reason, string? reasonName) : EntityEventArgs
{
    public readonly int Delta = delta;

    public readonly int Total = total;

    public readonly string Reason = reason;

    public readonly string? ReasonName = reasonName;
}
