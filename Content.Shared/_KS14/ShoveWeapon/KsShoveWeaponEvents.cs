using Robust.Shared.Audio;

namespace Content.Shared._KS14.ShoveWeapon;

/// <summary>
///     Raised on a held weapon when its holder tries to shove someone with it, before the roll. Handlers may change
///         the chance the shove fails, the sound of a successful one, or cancel it.
/// </summary>
[ByRefEvent]
public record struct KsDisarmWithWeaponAttemptEvent(EntityUid User, EntityUid Target, float Chance, SoundSpecifier SuccessSound)
{
    public bool Cancelled;

    /// <summary>
    ///     The chance the shove fails.
    /// </summary>
    public float Chance = Chance;

    public SoundSpecifier SuccessSound = SuccessSound;
}

/// <summary>
///     Raised on a held weapon after its holder shoved someone with it.
/// </summary>
[ByRefEvent]
public readonly record struct KsShovedWithWeaponEvent(EntityUid User, EntityUid Target);
