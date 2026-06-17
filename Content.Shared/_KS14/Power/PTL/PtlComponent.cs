using System.Numerics;
using Content.Shared.Damage;
using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Power.PTL;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PtlComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Active = false;

    [DataField, AutoNetworkedField]
    public double SpesosHeld = 0f;

    [DataField]
    public double SpesosMultiplier = 1.0;

    [DataField]
    public double MinShootPower = 1e6; // 1 MJ

    [DataField, AutoNetworkedField]
    public float ShootDelay = 10f;
    [DataField, AutoNetworkedField]
    public Vector2 ShootDelayThreshold = new(2f, 10f); // X is min, Y is max

    [DataField, AutoNetworkedField]
    public bool ReversedFiring = false;

    [DataField]
    public Vector2 ShootOffset = new(0, -1);

    [ViewVariables]
    public TimeSpan RadiationResetAt = TimeSpan.Zero;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextShotAt = TimeSpan.Zero;

    [DataField]
    public DamageSpecifier BaseBeamDamage = new();

    [DataField]
    public float DamageMultiplier = 2.0f;

    /// <summary>
    ///     Amount of power required to start emitting radiation and blinding people that come nearby
    /// </summary>
    [DataField] public double PowerEvilThreshold = 10; // 10 MJ;
}
