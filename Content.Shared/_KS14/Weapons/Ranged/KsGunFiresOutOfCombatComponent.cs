namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     Inverts the combat mode a gun needs: it fires only out of combat mode, and in combat mode it's left to its melee
///         attack. For tools that happen to be guns, like the fire extinguisher.
/// </summary>
[RegisterComponent]
public sealed partial class KsGunFiresOutOfCombatComponent : Component;
