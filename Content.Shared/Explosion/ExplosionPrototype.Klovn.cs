using Content.Shared.Whitelist;

namespace Content.Shared.Explosion;

public sealed partial class ExplosionPrototype
{
    /// <summary>
    ///     What this explosion leaves undamaged, such as the walls an anti-personnel round shouldn't breach. Null spares
    ///         nothing.
    /// </summary>
    [DataField]
    public EntityWhitelist? KsSpared;

    /// <summary>
    ///     Exceptions to <see cref="KsSpared"/>: damaged even when they match it. Null makes no exceptions.
    /// </summary>
    [DataField]
    public EntityWhitelist? KsNotSpared;
}
