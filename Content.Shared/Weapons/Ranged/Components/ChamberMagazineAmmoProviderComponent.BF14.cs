namespace Content.Shared.Weapons.Ranged.Components;

public sealed partial class ChamberMagazineAmmoProviderComponent
{
    /// <summary>
    ///     Whether the bolt locks back by itself when the last round is fired. When false, it stays closed on an empty
    ///         gun, and can still be opened and closed by hand.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool AutoOpenBoltOnEmpty = true;
}
