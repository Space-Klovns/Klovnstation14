using Content.Shared._KS14.Audio;
using Content.Shared._KS14.Chat;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EmoteAudioEffect;

public sealed class EmoteAudioEffectSystem : EntitySystem
{
    [Dependency] private readonly AudioEffectSystem _audioEffectSystem = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmoteAudioEffectComponent, EmoteSoundPlayedEvent>(OnEmoteSound);
        SubscribeLocalEvent<EmoteAudioEffectComponent, InventoryRelayedEvent<EmoteSoundPlayedEvent>>(OnEmoteSoundRelayed);
    }

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

    private void OnEmoteSoundRelayed(Entity<EmoteAudioEffectComponent> entity, ref InventoryRelayedEvent<EmoteSoundPlayedEvent> args) => OnEmoteSound(entity, ref args.Args);
}
