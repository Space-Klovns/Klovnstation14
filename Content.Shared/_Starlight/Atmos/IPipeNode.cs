using Content.Shared.Atmos.Components;
using Content.Shared.Atmos;

namespace Content.Shared._Starlight.Atmos;

public interface IPipeNode
{
    PipeDirection Direction { get; }
    AtmosPipeLayer Layer { get; }

    /// <summary>
    ///     Which kind of piping network this node belongs to.
    ///     Nodes of different kinds never share a network, so they never block each others placement either.
    /// </summary>
    PipeNodeKind Kind => PipeNodeKind.Atmospherics;
}

/// <summary>
///     The mutually exclusive families of piping that <see cref="IPipeNode"/>s can belong to.
/// </summary>
public enum PipeNodeKind : byte
{
    /// <summary>
    ///     Gas pipes, scrubbers, vents and so on.
    /// </summary>
    Atmospherics,

    /// <summary>
    ///     Reagent fluid ducts and plumbing machinery.
    /// </summary>
    Plumbing,
}
