using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Burning;

/// <summary>
///     An item slowly burning away, like one dropped in lava. tgstation's <c>/datum/component/burning</c>: it takes
///         heat damage every second until it is reduced to ash, unless it is put out.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentPause]
[Access(typeof(KsItemBurningSystem))]
public sealed partial class KsItemBurningComponent : Component
{
    /// <summary>
    ///     Applied every <see cref="TickInterval"/>, to the item's own damage if it has any. tgstation burns 10 a second
    ///         with lava capping fire armour at 50%.
    /// </summary>
    [DataField]
    public DamageSpecifier Damage = new() { DamageDict = new() { { "Heat", 5 } } };

    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     What is left to burn. Every tick takes away the total of <see cref="Damage"/>, and at zero the item turns to
    ///         ash, whether it has damage of its own or not. tgstation's default object integrity.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public float Integrity = 200f;

    [DataField, AutoPausedField]
    public TimeSpan NextTick;

    [DataField]
    public EntProtoId Ash = "KsAsh";

    /// <summary>
    ///     Left behind instead of <see cref="Ash"/> by huge items.
    /// </summary>
    [DataField]
    public EntProtoId AshLarge = "KsAshLarge";

    /// <summary>
    ///     Dealt to a bare hand that grabs the item.
    /// </summary>
    [DataField]
    public DamageSpecifier TouchDamage = new() { DamageDict = new() { { "Heat", 5 } } };

    [DataField]
    public SoundSpecifier TouchSound = new SoundCollectionSpecifier("MeatLaserImpact");
}
