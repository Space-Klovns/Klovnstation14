using Content.Shared.Chat.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.Audio.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Chat;

/// <summary>
///     Raised on an entity doing an emote, when its emote makes a sound.
/// </summary>
[ByRefEvent]
public record struct EmoteSoundPlayedEvent(Entity<AudioComponent> AudioEntity, ProtoId<EmotePrototype>? EmoteId, SlotFlags TargetSlots = SlotFlags.WITHOUT_POCKET) : IInventoryRelayEvent;
