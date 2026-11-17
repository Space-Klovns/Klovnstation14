using Content.Server.NodeContainer.NodeGroups;
using Content.Shared.Atmos.Components;

namespace Content.Server.Atmos.EntitySystems;

/// <summary>
///     KS14: pipe nets on a grid with no <see cref="GridAtmosphereComponent"/> (a map that gives its grids the map's immutable
///         atmosphere to save cost). The grid loop never sees them, so they are updated here instead, once per atmos cycle.
///         Nets move onto the grid's own list if the grid later gains an atmosphere.
/// </summary>
public sealed partial class AtmosphereSystem
{
    private readonly HashSet<PipeNet> _ksOffGridPipeNets = new();
    private readonly List<PipeNet> _ksOffGridScratch = new();

    /// <summary>
    ///     Tracks <paramref name="pipeNet"/> to be updated outside any grid's atmosphere.
    /// </summary>
    public void AddOffGridPipeNet(PipeNet pipeNet)
    {
        _ksOffGridPipeNets.Add(pipeNet);
    }

    public void RemoveOffGridPipeNet(PipeNet pipeNet)
    {
        _ksOffGridPipeNets.Remove(pipeNet);
    }

    /// <summary>
    ///     Updates every off-grid pipe net once. Called once per <see cref="RealAtmosTime"/> by the device system.
    /// </summary>
    public void UpdateOffGridPipeNets()
    {
        if (_ksOffGridPipeNets.Count == 0)
            return;

        _ksOffGridScratch.Clear();
        _ksOffGridScratch.AddRange(_ksOffGridPipeNets);

        foreach (var pipeNet in _ksOffGridScratch)
        {
            if (pipeNet.Removed)
            {
                _ksOffGridPipeNets.Remove(pipeNet);
                continue;
            }

            // The grid gained an atmosphere: the grid loop owns this net from now on.
            if (pipeNet.Grid is { } gridUid && AddPipeNet(gridUid, pipeNet))
            {
                _ksOffGridPipeNets.Remove(pipeNet);
                continue;
            }

            pipeNet.Update();
        }

        _ksOffGridScratch.Clear();
    }
}
