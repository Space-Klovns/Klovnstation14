using Content.Server.Decals;
using Content.Shared._KS14.Decals;
using Content.Shared.Decals;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._KS14.Decals;

/// <summary>
///     Adds a random decal of a tag to a tile, up to a limit: for <see cref="KsDecalOnSpawnComponent"/>, and for the burn
///         marks a hotspot leaves (<c>AtmosphereSystem.ProcessHotspot</c>).
/// </summary>
public sealed partial class KsDecalOnSpawnSystem : EntitySystem
{
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;

    /// <summary>
    ///     The ids of the decal prototypes with each tag asked for so far.
    /// </summary>
    private readonly Dictionary<string, string[]> _decalsByTag = new();

    [SubscribeLocalEvent]
    private void OnMapInit(Entity<KsDecalOnSpawnComponent> entity, ref MapInitEvent args)
    {
        var transformComponent = Transform(entity);
        if (transformComponent.GridUid is not { } gridUid ||
            !TryComp<MapGridComponent>(gridUid, out var gridComponent))
            return;

        var tile = _mapSystem.CoordinatesToTile(gridUid, gridComponent, transformComponent.Coordinates);
        TryAddTaggedDecal(gridUid, tile, entity.Comp.Tag, entity.Comp.MaxDecals);
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<DecalPrototype>())
            _decalsByTag.Clear();
    }

    /// <summary>
    ///     Adds a scorch mark to a tile, as a full tile fire does, unless it already has
    ///         <see cref="KsDecalTags.DefaultMaxDecals"/>.
    /// </summary>
    public bool TryAddBurnDecal(EntityUid gridUid, Vector2i tile)
    {
        return TryAddTaggedDecal(gridUid, tile, KsDecalTags.Burnt, KsDecalTags.DefaultMaxDecals);
    }

    /// <summary>
    ///     Adds a random cleanable decal with <paramref name="tag"/> to a tile, unless it already has
    ///         <paramref name="maxDecals"/> decals with that tag.
    /// </summary>
    /// <returns>Whether a decal was added.</returns>
    public bool TryAddTaggedDecal(EntityUid gridUid, Vector2i tile, string tag, int maxDecals)
    {
        var decalIds = GetDecalIds(tag);
        if (decalIds.Length == 0)
            return false;

        var taggedDecals = 0;
        foreach (var (_, decal) in _decalSystem.GetDecalsInRange(gridUid, tile))
        {
            if (Array.IndexOf(decalIds, decal.Id) == -1)
                continue;

            if (++taggedDecals >= maxDecals)
                return false;
        }

        return _decalSystem.TryAddDecal(decalIds[_random.Next(decalIds.Length)],
            new EntityCoordinates(gridUid, tile),
            out _,
            cleanable: true);
    }

    private string[] GetDecalIds(string tag)
    {
        if (_decalsByTag.TryGetValue(tag, out var decalIds))
            return decalIds;

        var matchingIds = new List<string>();
        foreach (var decalPrototype in ProtoMan.EnumeratePrototypes<DecalPrototype>())
        {
            if (decalPrototype.Tags.Contains(tag))
                matchingIds.Add(decalPrototype.ID);
        }

        decalIds = matchingIds.ToArray();
        _decalsByTag[tag] = decalIds;
        return decalIds;
    }
}
