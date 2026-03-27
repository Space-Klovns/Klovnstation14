using Robust.Shared.GameStates;
using Content.Shared.FixedPoint;

namespace Content.Shared._KS14.Weapons.Melee;

/// <summary>
/// Component that forces the target to unwield their weapon when hit with a melee attack.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MeleeForceUnwieldWeaponComponent : Component
{
        /// <summary>
        /// Damage types and underlying probability modifiers that cause this weapon to get unwielded upon melee hit.
        /// </summary>
        [DataField, AutoNetworkedField]
        public Dictionary<string, FixedPoint2> UnwieldDict { get; set; } = new()
        {
            ["Blunt"] = 2,
            ["Slash"] = 1.6,
            ["Pierce"] = 1
        };
}
