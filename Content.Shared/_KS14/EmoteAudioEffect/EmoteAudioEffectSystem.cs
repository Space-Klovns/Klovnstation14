using Content.Shared._KS14.Audio;
using Content.Shared._KS14.Chat;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EmoteAudioEffect;

public sealed partial class EmoteAudioEffectSystem : EntitySystem
{
    [Dependency] private AudioEffectSystem _audioEffectSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    [SubscribeLocalEvent]
    private void OnEmoteSound(Entity<EmoteAudioEffectComponent> entity, ref EmoteSoundPlayedEvent args)
    {
        if (args.EmoteId is { } emoteId)
        {
            if (!_prototypeManager.TryIndex(emoteId, out var emotePrototype))
                return;

            if (!entity.Comp.EmoteCategory.HasFlag(emotePrototype.Category))
                return;
        }

        _audioEffectSystem.TryAddEffect(args.AudioEntity, entity.Comp.PresetId);
    }

    [SubscribeLocalEvent]
    private void OnEmoteSoundRelayed(Entity<EmoteAudioEffectComponent> entity, ref InventoryRelayedEvent<EmoteSoundPlayedEvent> args) => OnEmoteSound(entity, ref args.Args);
}
