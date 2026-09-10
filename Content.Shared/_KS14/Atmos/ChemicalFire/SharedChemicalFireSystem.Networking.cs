using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.Atmos.ChemicalFire;

public abstract partial class SharedChemicalFireSystem : EntitySystem
{
    /// <summary>
    ///     Scratch set of the tiles touched by a state application - the union of what the grid held before
    ///         and what the state brings - so both additions and removals get resmoothed.
    /// </summary>
    private readonly HashSet<Vector2i> _resmoothTiles = [];

    private void InitialiseNetworking()
    {
        SubscribeLocalEvent<ChemicalFireGridComponent, ComponentGetState>(OnGridGetState);
        SubscribeLocalEvent<ChemicalFireGridComponent, ComponentHandleState>(OnGridHandleState);
    }

    private void OnGridGetState(Entity<ChemicalFireGridComponent> entity, ref ComponentGetState args)
    {
        var tiles = new Dictionary<Vector2i, TileChemicalFireData<NetEntity>>(entity.Comp.Tiles.Count);

        foreach (var (tile, tileData) in entity.Comp.Tiles)
        {
            var netTileData = new TileChemicalFireData<NetEntity>();

            foreach (var (connectionKey, fire) in tileData.Fires)
            {
                // Fires that are already on their way out would resolve to nothing on the client.
                if (!EntityManager.MetaQuery.TryGetComponent(fire.Owner, out var metaDataComponent) ||
                    Terminating(fire.Owner, metaData: metaDataComponent))
                    continue;

                netTileData.Fires[connectionKey] = GetNetEntity(fire.Owner, metadata: metaDataComponent);
            }

            if (netTileData.Fires.Count == 0)
                continue;

            tiles[tile] = netTileData;
        }

        args.State = new ChemicalFireGridComponentState(tiles);
    }

    private void OnGridHandleState(Entity<ChemicalFireGridComponent> entity, ref ComponentHandleState args)
    {
        if (args.Current is not ChemicalFireGridComponentState state)
            return;

        // Tiles that the state drops still need a resmooth, so remember what we had before replacing it.
        _resmoothTiles.Clear();
        foreach (var tile in entity.Comp.Tiles.Keys)
            _resmoothTiles.Add(tile);

        // Full replace: the server's view is authoritative, and any client-predicted chemfire re-registers
        //     itself when prediction re-runs on top of the applied state.
        entity.Comp.Tiles.Clear();

        foreach (var (tile, netTileData) in state.Tiles)
        {
            _resmoothTiles.Add(tile);
            var tileData = entity.Comp.Tiles.GetOrNew(tile);

            foreach (var (connectionKey, netFire) in netTileData.Fires)
            {
                var fireUid = GetEntity(netFire);
                if (!fireUid.IsValid())
                    continue;

                // The chemfire's own state may not have been applied yet, so ensure rather than resolve.
                var fireComponent = _chemicalFireQuery.CompOrNull(fireUid) ?? EnsureComp<ChemicalFireComponent>(fireUid);

                tileData.Fires[connectionKey] = (fireUid, fireComponent);
            }

            if (tileData.Fires.Count == 0)
                entity.Comp.Tiles.Remove(tile);
        }

        foreach (var tile in _resmoothTiles)
            RaiseTileChanged(entity.Owner, tile);

        _resmoothTiles.Clear();
    }
}
