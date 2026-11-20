using Content.Shared.Actions;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Perks.Support;

/// <summary>
///     The Mark Target action: an enemy (a living mob that isn't the holder's friend) is marked for
///         <see cref="Duration"/> (<see cref="KsMarkedTargetComponent"/>), outlined for the holder's side and taking
///         <see cref="DamageTakenMultiplier"/> times the damage from everything. Worked out on the server
///         (<c>KsMarkTargetSystem</c>).
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class KsMarkTargetAbilityComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(8);

    [DataField]
    public float DamageTakenMultiplier = 1.2f;

    [DataField]
    public Color OutlineColor = Color.FromHex("#ff3030");

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Machines/twobeep.ogg");
}

public sealed partial class KsMarkTargetActionEvent : EntityTargetActionEvent;
