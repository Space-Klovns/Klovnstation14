using Content.Server.Explosion.EntitySystems;
using Content.Shared._KS14.Throwing;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Explosion;

/// <summary>
///     A mob that explosions send flying. Stock explosions throw only loose things (dynamic bodies), which a mob
///         isn't. How far and how fast depends on the blast's throw force where the mob stands, and whatever the mob
///         then slams into knocks it over and hurts it, as it does someone thrown out of a grab
///         (<see cref="KsThrownSlamSystem"/>).
/// </summary>
[RegisterComponent, Access(typeof(ExplosionSystem))]
public sealed partial class KsExplosionThrownComponent : Component
{
    /// <summary>
    ///     The weakest throw force that lifts the mob at all. An explosion's throw force on a tile is ten times the
    ///         square root of its intensity there, so 15 is an intensity of 2.25.
    /// </summary>
    [DataField]
    public float MinThrowForce = 15f;

    /// <summary>
    ///     Throw speed, in metres per second, for each point of throw force.
    /// </summary>
    [DataField]
    public float SpeedPerForce = 0.4f;

    [DataField]
    public float MaxSpeed = 20f;

    /// <summary>
    ///     How far the mob is thrown, in metres, for each point of throw force.
    /// </summary>
    [DataField]
    public float DistancePerForce = 0.15f;

    [DataField]
    public float MaxDistance = 8f;

    /// <summary>
    ///     How long the blast takes the mob off its feet for. Null leaves it standing as it flies.
    /// </summary>
    [DataField]
    public TimeSpan? KnockdownTime = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     What happens when the mob slams into something.
    /// </summary>
    [DataField]
    public ProtoId<KsThrownSlamPrototype> Slam = "KsThrownSlam";
}
