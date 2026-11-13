using Robust.Shared.GameStates;

namespace Content.Shared.Damage.Components;

public sealed partial class DamagedByContactComponent
{
    /// <summary>
    ///     Copied from the <see cref="DamageContactsComponent.OnlyWhileMoving"/> of what this entity touched, as
    ///         <see cref="Damage"/> is.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool OnlyWhileMoving;
}
