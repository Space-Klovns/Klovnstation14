using Content.Shared.Chat.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EmoteAudioEffect;

/// <summary>
///     Asks what effect an entity's gear puts on a sound of <see cref="Category"/> it makes, without an audio entity to
///         put it on. Relayed to worn items, so a mask answers for its wearer. For sounds that aren't audio entities,
///         such as voice chat; see <see cref="EmoteAudioEffectSystem.GetEffect"/>.
/// </summary>
[ByRefEvent]
public record struct EmoteAudioEffectQueryEvent(EmoteCategory Category, SlotFlags TargetSlots = SlotFlags.WITHOUT_POCKET)
    : IInventoryRelayEvent
{
    public ProtoId<AudioPresetPrototype>? Preset;
}
