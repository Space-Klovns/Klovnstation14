namespace Content.Shared._KS14.Medical.IvDrip;

/// <summary>
///     Tracks which IV drips an entity is currently wearing.
/// </summary>
/// <remarks>
///     Exists so that something interested in "this entity got hurt while wearing a drip" can subscribe
///         to a damage event on the wearer directly. The inventory relay cannot serve that: the only
///         damage event it carries is <c>DamageModifyEvent</c>, which is an armour hook raised before
///         the damage is known to land at all.
///     Pure runtime bookkeeping maintained by <see cref="IvDripSystem"/> on equip and unequip -
///         never saved, never networked.
/// </remarks>
[RegisterComponent]
[Access(typeof(IvDripSystem))]
public sealed partial class IvDripWearerComponent : Component
{
    /// <summary>
    ///     Every drip this entity currently has equipped.
    /// </summary>
    [ViewVariables]
    public HashSet<EntityUid> DripUids = [];
}
