using Content.Shared.Mobs;

namespace Content.Shared._KS14.FieldCommander;

/// <summary>
///     The one way to write a <see cref="KsFieldCommanderDefenderComponent"/>, which the server's rule calls as it
///         prepares a defender.
/// </summary>
public sealed class KsFieldCommanderDefenderSystem : EntitySystem
{
    /// <summary>
    ///     Gives a defender its down flashes: a copy of the dictionary, so that no two components share one. The
    ///         settings in it are shared, and read-only.
    /// </summary>
    public void SetFlashes(Entity<KsFieldCommanderDefenderComponent> entity, Dictionary<MobState, KsFieldCommanderDownFlashSettings> flashes)
    {
        entity.Comp.Flashes = new Dictionary<MobState, KsFieldCommanderDownFlashSettings>(flashes);
        Dirty(entity);
    }
}
