using Robust.Shared.Containers;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.EntityProcessor;

/// <summary>
///     For generic machines or whatever that turn one thing into another, on collision.
///         Requires extra logic or whatever.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(KsEntityProcessorSystem))]
public sealed partial class KsEntityProcessorComponent : Component
{
    /// <summary>
    ///     Contains things that are being processed.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Container Container = default!;

    [DataField]
    [AutoNetworkedField]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool Powered = false;

    /// <summary>
    ///     Fixture ID that, upon contact, things will be processed.
    ///         No fixture is automatically added.
    ///
    ///     If null, things won't be processed upon contact.
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadWrite)]
    public string? FixtureId = null;
}

/// <summary>
///     Raised on a processor before the entity processed to check if it can
///         be processed, and to modify the game-time at which it will finish
///         being processed.
/// </summary>
/// <param name="ProcessingFinishTime">By default, this is the game-time at which this event was raised.</param>
[ByRefEvent]
public record struct KsAttemptProcessEntityEvent(bool Cancelled, Entity<KsEntityProcessorComponent> ProcessorEntity, EntityUid ProcessedUid, TimeSpan ProcessingFinishTime);

/// <summary>
///     Raised on a processor when an entity starts being processed, before
///         it is forced into the processor container.
/// </summary>
[ByRefEvent]
public record struct KsStartedProcessingEntityEvent(Entity<KsEntityProcessorComponent, TransformComponent> ProcessorEntity, EntityUid ProcessedUid);

/// <summary>
///     Raised on a processor when an entity is done being processed.
/// </summary>
[ByRefEvent]
public record struct KsFinishedProcessingEntityEvent(Entity<KsEntityProcessorComponent, KsActiveEntityProcessorComponent?> Entity, EntityUid ProcessedUid);

/// <summary>
///     Raised on an active processor after an entity is removed from the processor container;
///         can be either on successful processing or fail.
///
///     Not automatically raised by base processor code when something has <i>finished</i> processing.
/// </summary>
[ByRefEvent]
public record struct KsEntityRemovedFromActiveProcessorEvent(Entity<KsActiveEntityProcessorComponent> Entity, EntityUid ProcessedUid);

/// <summary>
///     Raised on a processor when there is nothing left to process.
/// </summary>
[ByRefEvent]
public record struct KsFinishedProcessingEverythingEvent;
