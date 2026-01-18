using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Trauma - moved to shared, modernized the datafields
/// Plays the specified sound upon receiving damage of that type.
/// </summary>
[RegisterComponent]
public sealed partial class RangedDamageSoundComponent : Component
{
    // TODO: Limb damage changing sound type.

    /// <summary>
    /// Specified sounds to apply when the entity takes damage with the specified group.
    /// Will fallback to defaults if none specified.
    /// </summary>
<<<<<<< HEAD:Content.Shared/Weapons/Ranged/Components/RangedDamageSoundComponent.cs
    [DataField]
    public Dictionary<ProtoId<DamageGroupPrototype>, SoundSpecifier>? SoundGroups;
=======
    [DataField(customTypeSerializer: typeof(PrototypeIdDictionarySerializer<SoundSpecifier, DamageGroupPrototype>))]
    public Dictionary<string, SoundSpecifier>? SoundGroups;
>>>>>>> upstream/master:Content.Server/Weapons/Ranged/Components/RangedDamageSoundComponent.cs

    /// <summary>
    /// Specified sounds to apply when the entity takes damage with the specified type.
    /// Will fallback to defaults if none specified.
    /// </summary>
<<<<<<< HEAD:Content.Shared/Weapons/Ranged/Components/RangedDamageSoundComponent.cs
    [DataField]
    public Dictionary<ProtoId<DamageTypePrototype>, SoundSpecifier>? SoundTypes;
=======
    [DataField(customTypeSerializer: typeof(PrototypeIdDictionarySerializer<SoundSpecifier, DamageTypePrototype>))]
    public Dictionary<string, SoundSpecifier>? SoundTypes;
>>>>>>> upstream/master:Content.Server/Weapons/Ranged/Components/RangedDamageSoundComponent.cs
}
