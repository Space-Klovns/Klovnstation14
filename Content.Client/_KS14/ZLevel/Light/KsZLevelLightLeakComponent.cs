using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.ZLevel.Light;

/// <summary>
///     Attached to a light that is currently shining down onto the z-levels below it, holding the stand-in
///         lights that do the shining and the gaps in the floor they shine through.
/// </summary>
/// <remarks>
///     Client-side only, and added and removed by <see cref="KsZLevelLightLeakSystem"/> alone: its presence
///         means "this light has stand-ins that need cleaning up", which is the only reason it exists.
/// </remarks>
[RegisterComponent]
[Access(typeof(KsZLevelLightLeakSystem))]
public sealed partial class KsZLevelLightLeakComponent : Component
{
    /// <summary>
    ///     One stand-in per gap this light reaches, per z-level below it that it reaches.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> StandIns = [];

    /// <summary>
    ///     The gaps in this light's own floor that it can shine through, nearest first, in world coordinates.
    /// </summary>
    [ViewVariables]
    public List<Vector2> Holes = [];

    /// <summary>
    ///     The mask the stand-ins were last given, so it is only resolved again when it actually changes.
    /// </summary>
    /// <remarks>
    ///     Resolving one is a prototype lookup and a texture-cache fetch, which is not something to do for
    ///         every light on the station every frame.
    /// </remarks>
    [ViewVariables]
    public ProtoId<LightMaskPrototype>? AppliedMask;

    #region Hole search

    /*
        Finding the gaps means scanning every tile within a light's radius, which is far too much to do for
            every light on the station every frame. Almost every light is bolted to a wall and never moves, so
            the answer is cached and only recomputed when something that could change it does.
    */

    /// <summary>Whether <see cref="Holes"/> has been filled in at all yet.</summary>
    [ViewVariables]
    public bool Searched;

    /// <summary>Where the light was, and how far it reached, when the gaps were last looked for.</summary>
    [ViewVariables]
    public Vector2 SearchPosition;

    [ViewVariables]
    public float SearchRadius;

    [ViewVariables]
    public MapId SearchMapId = MapId.Nullspace;

    /// <summary>
    ///     When to look again regardless, which is what notices a floor being built or cut under a light that
    ///         has not moved.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextSearch;

    #endregion
}
