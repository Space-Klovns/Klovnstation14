using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Perks.Tank;

/// <summary>
///     An action that raises a shield soaking all damage up to <see cref="Capacity"/>. While it is up the holder is
///         slowed and can't fire; it breaks loudly when spent, and can be raised again after <see cref="Cooldown"/>
///         from when it went down.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(KsLivingShieldSystem))]
public sealed partial class KsLivingShieldAbilityComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Capacity = 150f;

    [DataField, AutoNetworkedField]
    public float WalkModifier = 0.6f;

    [DataField, AutoNetworkedField]
    public float SprintModifier = 0.6f;

    [DataField, AutoNetworkedField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [DataField]
    public SoundSpecifier? RaiseSound = new SoundPathSpecifier("/Audio/Weapons/block_metal1.ogg");

    [DataField]
    public SoundSpecifier? LowerSound = new SoundPathSpecifier("/Audio/Weapons/block_metal1.ogg");

    [DataField]
    public SoundSpecifier? BreakSound = new SoundPathSpecifier("/Audio/Effects/metal_glass_break1.ogg", AudioParams.Default.WithVolume(6f));

    /// <summary>
    ///     Drawn on the holder while the shield is up.
    /// </summary>
    [DataField]
    public SpriteSpecifier.Rsi ShieldSprite = new(new ResPath("Objects/Weapons/Melee/shields.rsi"), "riot-inhand-right");

    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public bool Raised;

    /// <summary>
    ///     Damage it can still soak.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public float Remaining;

    /// <summary>
    ///     The action that raised it, for its toggle and cooldown.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? Action;
}

public sealed partial class KsLivingShieldActionEvent : InstantActionEvent;
