// KS14: added in this fork
using Content.Shared._KS14.TTS;
using Robust.Shared.Prototypes;

namespace Content.Shared.Preferences;

// Partial so the fork's profile fields live apart from the upstream file.
public sealed partial class HumanoidCharacterProfile
{
    /// <summary>
    ///     The TTS voice this character speaks with, or null to be given a random one each round.
    ///         Only <see cref="TtsVoicePrototype.Selectable"/> voices survive <see cref="EnsureValid"/>.
    /// </summary>
    [DataField]
    public ProtoId<TtsVoicePrototype>? TtsVoice { get; private set; }

    public HumanoidCharacterProfile WithTtsVoice(ProtoId<TtsVoicePrototype>? voice)
    {
        return new(this) { TtsVoice = voice };
    }

    /// <summary>
    ///     Whether a player may pick <paramref name="voice"/> for a character. Null, "random", always may.
    /// </summary>
    public static bool IsSelectableTtsVoice(ProtoId<TtsVoicePrototype>? voice, IPrototypeManager prototypeManager)
    {
        return voice is not { } id ||
               prototypeManager.TryIndex(id, out var prototype) && prototype.Selectable;
    }

    private void EnsureValidTtsVoice(IPrototypeManager prototypeManager)
    {
        // A voice that was removed, or made unselectable since, falls back to random rather than failing the profile.
        if (!IsSelectableTtsVoice(TtsVoice, prototypeManager))
            TtsVoice = null;
    }
}
