using Content.Client.Decals;
using Content.Shared.Decals;

namespace Content.Client._KS14.Decals;

/// <summary>
///     On a grid: decals this client predicted for something that is never re-predicted (the hit of its kept copy of its
///         own bullet), drawn with the grid's decals until the server's own arrive.
/// </summary>
/// <remarks>
///     A predicted decal is ordinarily written into the networked <see cref="DecalChunkComponent"/>, where the next state
///         puts the chunk back and the prediction, run again, writes it again. Nothing runs a kept copy's hit again, so
///         its decals showed for a tick, went, and came back with the server's. These are kept apart from the chunk,
///         so no state touches them. See <see cref="DecalSystem.KsAddKeptPredictedDecal"/>.
/// </remarks>
[RegisterComponent, Access(typeof(DecalSystem))]
public sealed partial class KsKeptPredictedDecalsComponent : Component
{
    [ViewVariables]
    public List<KsKeptPredictedDecal> Decals = new();
}

/// <param name="Decal">The decal, placed in the grid's frame as any of its decals is.</param>
/// <param name="ExpiresAt">The time after which the server's is taken never to be coming.</param>
public readonly record struct KsKeptPredictedDecal(Decal Decal, TimeSpan ExpiresAt);
