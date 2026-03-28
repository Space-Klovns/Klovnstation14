using Content.Shared.Chat.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.EmoteAudioEffect;

[RegisterComponent]
[NetworkedComponent]
public sealed partial class EmoteAudioEffectComponent : Component
{
    /// <summary>
    ///     Emote category that will get this effect.
    /// </summary>
    [DataField]
    public EmoteCategory EmoteCategory = EmoteCategory.Vocal;

    /// <summary>
    ///     Audio preset to apply as an effect.
    /// </summary>
    [DataField]
    public ProtoId<AudioPresetPrototype> PresetId = "MuffledMask";
}
