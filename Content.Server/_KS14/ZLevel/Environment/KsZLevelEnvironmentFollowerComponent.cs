namespace Content.Server._KS14.ZLevel.Environment;

/// <summary>
///     A map whose environment was copied from <see cref="Source"/>, and which keeps following the changes made to it
///         by whatever runs it (weather cycles, for one).
/// </summary>
[RegisterComponent, Access(typeof(KsZLevelEnvironmentSystem))]
public sealed partial class KsZLevelEnvironmentFollowerComponent : Component
{
    /// <summary>
    ///     The map followed. Always the root of a chain: a level built on top of a follower follows that one's source.
    /// </summary>
    [DataField]
    public EntityUid? Source;
}
