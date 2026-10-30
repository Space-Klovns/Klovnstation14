using Robust.Shared.GameStates;

namespace Content.Shared._KS14.ZLevel.Transit;

/// <summary>
///     Marks a map as the space between two z-levels, so that a grid can be somewhere other than standing
///         on a floor plane.
/// </summary>
/// <remarks>
///     A gap map is deliberately <em>not</em> a member of the stack it sits in. Inserting one would partition
///         the depth interval between <see cref="LowerZLevel"/> and <see cref="UpperZLevel"/>, and every
///         consumer of <see cref="KsZLevelComponent.Depth"/> reads that interval as "the distance to the level
///         above" - so floor numbering, elevator navigation, PVS mirroring and the light-from-above pass would
///         all quietly start answering about a gap instead of a floor.
///     Instead a gap is <em>anchored</em>: it names the z-level below it, and <see cref="KsZLevelSystem"/>'s
///         navigation API answers every stack question about a gap map as though it had been asked about that
///         anchor, offset by <see cref="Progress"/>. Nothing else in the codebase has to know gaps exist -
///         audio leak, light leak and the renderer all go through that API already.
/// </remarks>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
[Access(typeof(SharedKsZLevelGapSystem))]
public sealed partial class KsZLevelGapComponent : Component
{
    /// <summary>
    ///     The z-level below this gap, which every stack question about it is answered about.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid LowerZLevel;

    /// <summary>
    ///     The z-level above this gap.
    /// </summary>
    /// <remarks>
    ///     Held rather than looked up so that teardown still knows where a grid was going after the stack has
    ///         been relinked out from under it mid-flight.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public EntityUid UpperZLevel;

    /// <summary>
    ///     <see cref="LowerZLevel"/>'s <see cref="KsZLevelComponent.Depth"/> as it was when this gap was made.
    /// </summary>
    /// <remarks>
    ///     Captured rather than read live, so that an admin retuning the level's depth mid-flight cannot make
    ///         whatever is in the gap jump. The gap simply finishes its crossing against the geometry it
    ///         started in.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public float TotalDepth = 1f;

    /// <summary>
    ///     How far up the gap this is, from 0 at <see cref="LowerZLevel"/>'s floor plane to 1 at
    ///         <see cref="UpperZLevel"/>'s.
    /// </summary>
    /// <remarks>
    ///     The same 0..1 convention <see cref="Physics.KsZLevelTransitComponent.Height"/> uses for a falling
    ///         entity. Multiplied by <see cref="TotalDepth"/> it is a real distance, which is what makes the
    ///         renderer's depth accumulation and the audio and light attenuation come out right without any
    ///         of them being told a gap is involved.
    /// </remarks>
    /// <seealso cref="SharedKsZLevelGapSystem.SetGapProgress"/>
    [DataField, AutoNetworkedField]
    public float Progress;

    /// <summary>
    ///     The grid this gap exists to carry.
    /// </summary>
    /// <remarks>
    ///     A gap outlives nothing: once this is gone, or has left the map, the sweep in
    ///     <see cref="SharedKsZLevelGapSystem"/> deletes the map. Without that, an elevator blown up mid-flight
    ///         would leave a dead map behind for the rest of the round.
    /// </remarks>
    [DataField, AutoNetworkedField]
    public EntityUid? PrimaryGrid;
}
