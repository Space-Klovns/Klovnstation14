using Content.Shared.Alert;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     A called rally (<see cref="KsRallyAbilityComponent"/>): a ring of <see cref="Radius"/> that fills in until
///         <see cref="ActivateTime"/>, when everyone friendly inside is rallied and it flashes and fades. Set up by the
///         server when the rally is called; drawn by the client.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KsRallyZoneComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Radius = 3f;

    [DataField, AutoNetworkedField]
    public Color Color = Color.FromHex("#ffcc33");

    /// <summary>
    ///     From being put down to going off, for a zone nobody called (placed by hand): the countdown starts at map
    ///         init, so it fills in and fades like a called one rather than going off and vanishing within two ticks. A
    ///         call replaces it with the ability's own <see cref="KsRallyAbilityComponent.Delay"/>.
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(1.5);

    /// <summary>
    ///     How long it takes to fade once it has gone off.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan FadeDuration = TimeSpan.FromSeconds(0.6);

    /// <summary>
    ///     When it was called.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime;

    /// <summary>
    ///     When it goes off.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField, AutoPausedField]
    public TimeSpan ActivateTime;

    [ViewVariables(VVAccess.ReadOnly)]
    public bool Activated;

    /// <summary>
    ///     Who called it; its friends are rallied.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public EntityUid? CallerUid;

    [ViewVariables(VVAccess.ReadOnly)]
    public HashSet<ProtoId<NpcFactionPrototype>> Factions = new();

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan Duration;

    [ViewVariables(VVAccess.ReadOnly)]
    public float SpeedMultiplier = 1f;

    [ViewVariables(VVAccess.ReadOnly)]
    public float FireRateMultiplier = 1f;

    [ViewVariables(VVAccess.ReadOnly)]
    public float DamageMultiplier = 1f;

    [ViewVariables(VVAccess.ReadOnly)]
    public ProtoId<AlertPrototype>? Alert;

    [ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier? RallySound;

    [ViewVariables(VVAccess.ReadOnly)]
    public SoundSpecifier? EndSound;
}
