using Robust.Shared.Audio;

namespace Content.Shared._KS14.Damage.DamagedSound;

/// <summary>
///     Plays <see cref="Sound"/> when this entity is hit or damaged, and, with <see cref="PlayOnContactDamage"/>, when
///         its <see cref="Content.Shared.Damage.Components.DamageContactsComponent"/> hurts something touching it.
///         At most once per <see cref="Cooldown"/>.
/// </summary>
/// <remarks>
///     Not networked: the sound and cooldown come from the prototype, and <see cref="NextSound"/> is each side's own.
///         The server's limits what everyone hears from it; a client's only limits what it predicts itself.
/// </remarks>
[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(KsDamagedSoundSystem))]
public sealed partial class KsDamagedSoundComponent : Component
{
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    /// <summary>
    ///     The shortest time between two sounds from this entity, whatever played them.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(0.3);

    /// <summary>
    ///     Whether the sound also plays each time this entity's contact damage hurts something touching it.
    /// </summary>
    [DataField]
    public bool PlayOnContactDamage;

    /// <summary>
    ///     Until this time, nothing plays.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextSound;
}
