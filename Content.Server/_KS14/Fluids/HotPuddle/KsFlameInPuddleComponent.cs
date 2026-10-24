namespace Content.Server._KS14.Fluids.HotPuddle;

/// <summary>
///     A puddle that something hot is lying in, as of its last check (<see cref="KsHotPuddleSystem"/>). Its tile effects
///         run with an open flame of <see cref="KsHotPuddleSystem.SourceTemperature"/> in it until
///         <see cref="Until"/>, so a lit welder lights fuel whatever the fuel's temperature.
/// </summary>
/// <remarks>
///     Kept from the puddles' contacts, like <see cref="KsTouchingPuddleComponent"/>, so never saved.
/// </remarks>
[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
[Access(typeof(KsHotPuddleSystem), Other = AccessPermissions.Read)]
public sealed partial class KsFlameInPuddleComponent : Component
{
    /// <summary>
    ///     When the flame counts for no longer, unless a later check finds it still there.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan Until;
}
