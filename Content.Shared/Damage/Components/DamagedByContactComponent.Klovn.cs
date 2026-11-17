namespace Content.Shared.Damage.Components;

public sealed partial class DamagedByContactComponent
{
    /// <summary>
    ///     The sources this entity is touching whose damage it takes, each tick, each for itself: one for each
    ///         <see cref="DamageContactsComponent.ExclusiveGroup"/>, and one for every source without a group between
    ///         them. Kept by <see cref="Content.Shared.Damage.Systems.DamageContactsSystem"/> as contacts start and end;
    ///         a source stays the one for its group for as long as it is touched.
    /// </summary>
    /// <remarks>
    ///     Networked rather than worked out from contacts each tick: a client has no contacts for anyone's body but
    ///         its own player's.
    /// </remarks>
    [ViewVariables, AutoNetworkedField]
    public List<EntityUid> Sources = new();
}
