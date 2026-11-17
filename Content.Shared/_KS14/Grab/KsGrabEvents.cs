using System.Numerics;
using Content.Shared.Alert;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Grab;

/// <summary>
///     Raised on a puller who ctrl-clicks what they already pull, before the pull is let go. Handle it to do
///         something else with the click, as <see cref="KsGrabSystem"/> does to tighten a grip.
/// </summary>
[ByRefEvent]
public record struct KsPullToggleAttemptEvent(EntityUid Puller, EntityUid Pulled)
{
    public bool Handled;
}

/// <summary>
///     Raised on the grabber and the victim when the grab reaches a new stage, after its effects are applied.
/// </summary>
[ByRefEvent]
public readonly record struct KsGrabStageChangedEvent(EntityUid Grabber, EntityUid Victim, KsGrabStagePrototype? OldStage, KsGrabStagePrototype NewStage);

/// <summary>
///     Raised on the victim before a resist attempt is rolled. Systems can change the chance or cancel the attempt.
/// </summary>
[ByRefEvent]
public record struct KsGrabResistAttemptEvent(EntityUid Grabber, EntityUid Victim, float Chance)
{
    public float Chance = Chance;
    public bool Cancelled;
}

/// <summary>
///     Finishes tightening a grip that takes time.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class KsGrabUpgradeDoAfterEvent : SimpleDoAfterEvent
{
    /// <summary>
    ///     The stage the grip was at when the grabber started, so a grip that changed meanwhile isn't skipped ahead.
    /// </summary>
    [DataField]
    public int FromStage;

    public KsGrabUpgradeDoAfterEvent(int fromStage)
    {
        FromStage = fromStage;
    }

    public override DoAfterEvent Clone() => this;
}

/// <summary>
///     Raised on someone brought to their thrower's position to be thrown from it, on a client predicting the throw.
///     Their sprite slides there over <see cref="Duration"/>, from where it was drawn, rather than jumping.
/// </summary>
/// <param name="Offset">Where they were, relative to where they are now, in their parent's frame.</param>
[ByRefEvent]
public readonly record struct KsThrownFromHandsEvent(Vector2 Offset, TimeSpan Duration);

/// <summary>
///     <see cref="KsThrownFromHandsEvent"/> for everyone who sees the throw but the thrower, whose own client
///         predicts it.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsThrownFromHandsMessage(NetEntity thrown, Vector2 offset, TimeSpan duration) : EntityEventArgs
{
    public readonly NetEntity Thrown = thrown;
    public readonly Vector2 Offset = offset;
    public readonly TimeSpan Duration = duration;
}

/// <summary>
///     Clicking a grab alert as the victim tries to break free.
/// </summary>
public sealed partial class KsResistGrabAlertEvent : BaseAlertEvent;
