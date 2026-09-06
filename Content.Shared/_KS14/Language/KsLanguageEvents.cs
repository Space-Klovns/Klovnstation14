using Content.Shared.Examine;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._KS14.Language;

/// <summary>
///     Aggregation bus for language recomputes: the kernel seeds intrinsic knowledge, the
///     hands/inventory/implant relays fan it out to carried grant sources. Fresh collections
///     every raise; never cache one.
/// </summary>
[ByRefEvent]
public record struct KsRefreshLanguagesEvent(
    EntityUid Holder,
    HashSet<ProtoId<KsLanguagePrototype>> Spoken,
    HashSet<ProtoId<KsLanguagePrototype>> Understood) : IInventoryRelayEvent
{
    SlotFlags IInventoryRelayEvent.TargetSlots => SlotFlags.WITHOUT_POCKET;
}

/// <summary>
///     Client request to switch its spoken language; validated server-side.
/// </summary>
[Serializable, NetSerializable]
public sealed class KsSetLanguageMessage : EntityEventArgs
{
    public ProtoId<KsLanguagePrototype> Language;
}

/// <summary>
///     Voice-trigger examine hook for the server language system. A wrapper because the engine
///     allows only one directed ExaminedEvent subscription per component.
/// </summary>
public sealed class KsVoiceTriggerExaminedEvent(ExaminedEvent examine) : EntityEventArgs
{
    public readonly ExaminedEvent Examine = examine;
}
