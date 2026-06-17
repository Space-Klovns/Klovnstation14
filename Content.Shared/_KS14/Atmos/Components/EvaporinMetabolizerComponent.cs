using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Atmos.Components;

[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class EvaporinMetabolizerComponent : Component
{
    /// <summary>
    /// How much thirst is subtracted per mole of Evaporin inhaled.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float ThirstMultiplier = 1000000f; // Evaporin is VERY drying.

    /// <summary>
    /// Damage dealt when the entity is completely thirsty AND inhaling Evaporin.
    /// Scaled by moles inhaled.
    /// </summary>
    [DataField, AutoNetworkedField]
    public DamageSpecifier Damage = new()
    {
        DamageDict = new Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2>
        {
            { "Poison", FixedPoint2.New(500) } // High value because it's multiplied by moles (which are small in a breath)
        }
    };
}
