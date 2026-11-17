using Robust.Shared.GameStates;

namespace Content.Shared._KS14.Weapons.Ranged;

/// <summary>
///     On a full-auto gun whose <see cref="User"/> went down with the trigger held: they keep their grip, and the gun
///         keeps firing until it runs dry or they recover. Nothing here is saved, as a spasm doesn't outlast a map load.
/// </summary>
/// <seealso cref="KsTriggerSpasmSystem"/>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(KsTriggerSpasmSystem))]
public sealed partial class KsTriggerSpasmComponent : Component
{
    /// <summary>
    ///     Who is gripping the gun.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid User;

    /// <summary>
    ///     Where the gun is pointing, in world space. Kept here because the client of someone who is down still sends
    ///         shoot requests, which overwrite the gun's own coordinates.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public Angle AimAngle;

    /// <summary>
    ///     Which way recoil walks <see cref="AimAngle"/>: 1 or -1.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public int DriftSign = 1;
}
