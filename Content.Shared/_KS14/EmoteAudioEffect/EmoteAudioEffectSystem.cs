using Content.Shared._KS14.Audio;
using Content.Shared._KS14.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EmoteAudioEffect;

public sealed partial class EmoteAudioEffectSystem : EntitySystem
{
    [Dependency] private AudioEffectSystem _audioEffectSystem = default!;

    /// <summary>
    ///     The effect an entity's gear puts on a sound of this category it makes (a mask muffling a voice), if any. For
    ///         sounds that aren't audio entities; those raise <see cref="EmoteSoundPlayedEvent"/> instead.
    /// </summary>
    public ProtoId<AudioPresetPrototype>? GetEffect(EntityUid sourceUid, EmoteCategory category)
    {
        var ev = new EmoteAudioEffectQueryEvent(category);
        RaiseLocalEvent(sourceUid, ref ev);
        return ev.Preset;
    }

    [SubscribeLocalEvent]
    private void OnEmoteSound(Entity<EmoteAudioEffectComponent> entity, ref EmoteSoundPlayedEvent args)
    {
        EmoteCategory? category = null;
        if (args.EmoteId is { } emoteId)
        {
            if (!ProtoMan.TryIndex(emoteId, out var emotePrototype))
                return;

            category = emotePrototype.Category;
        }

        if (Affects(entity.Comp, category))
            _audioEffectSystem.TryAddEffect(args.AudioEntity, entity.Comp.PresetId);
    }

    [SubscribeLocalEvent]
    private void OnEmoteSoundRelayed(Entity<EmoteAudioEffectComponent> entity, ref InventoryRelayedEvent<EmoteSoundPlayedEvent> args) => OnEmoteSound(entity, ref args.Args);

    [SubscribeLocalEvent]
    private void OnEffectQuery(Entity<EmoteAudioEffectComponent> entity, ref EmoteAudioEffectQueryEvent args)
    {
        if (Affects(entity.Comp, args.Category))
            args.Preset = entity.Comp.PresetId;
    }

    [SubscribeLocalEvent]
    private void OnEffectQueryRelayed(Entity<EmoteAudioEffectComponent> entity, ref InventoryRelayedEvent<EmoteAudioEffectQueryEvent> args) => OnEffectQuery(entity, ref args.Args);

    /// <summary>
    ///     Whether the effect applies to a sound of this category. A sound of no known category (TTS, for one) always
    ///         gets it.
    /// </summary>
    private static bool Affects(EmoteAudioEffectComponent component, EmoteCategory? category)
        => category is not { } known || component.EmoteCategory.HasFlag(known);
}
