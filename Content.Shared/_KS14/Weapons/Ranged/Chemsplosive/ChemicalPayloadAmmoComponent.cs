using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Weapons.Ranged.Chemsplosive;

/// <summary>
///     Transfers a chem payload from this entity to the first projectile fired by it.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ChemicalPayloadAmmoComponent : Component;
