namespace Content.Server._KS14.Fluids.HotPuddle;

/// <summary>
///     Something that is touching at least one puddle, or left the last one less than a check ago. Every <see cref="KsHotPuddleSystem.CheckInterval"/> it is asked
///         whether it is hot (<see cref="Content.Shared.Temperature.IsHotEvent"/>), and if it is, it heats the
///         puddles it touches.
/// </summary>
/// <remarks>
///     Kept from the puddles' contacts, so never saved: they come back on their own once a map is loaded.
/// </remarks>
[RegisterComponent, UnsavedComponent, AutoGenerateComponentPause]
[Access(typeof(KsHotPuddleSystem), Other = AccessPermissions.Read)]
public sealed partial class KsTouchingPuddleComponent : Component
{
    /// <summary>
    ///     The puddles it is touching. Once this is empty, the component is removed at the next check, not at once,
    ///         so that leaving and coming back does not bring the check forward.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Puddles = [];

    /// <summary>
    ///     When it is next asked whether it is hot.
    /// </summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextCheck;
}
