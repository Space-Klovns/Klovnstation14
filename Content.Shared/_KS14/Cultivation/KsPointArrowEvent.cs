using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Cultivation;

/// <summary>
///     Sent to one player: show an arrow around them pointing at a place for a few seconds (where to take the last step
///         to the peak, say).
/// </summary>
[Serializable, NetSerializable]
public sealed class KsPointArrowEvent : EntityEventArgs
{
    public readonly MapCoordinates Target;
    public readonly float Seconds;
    public readonly string Label;

    public KsPointArrowEvent(MapCoordinates target, float seconds, string label)
    {
        Target = target;
        Seconds = seconds;
        Label = label;
    }
}
