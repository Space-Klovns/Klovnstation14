#nullable enable
using System.Collections.Generic;
using Content.Shared._KS14.Chain;
using Content.Shared._KS14.LinkedChain;
using Content.Shared.Trigger;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.Chain;

/// <summary>
///     Records the chain events and the triggers that reach a wire end, so tests can count them.
///         The test assembly's systems are registered on both sides; only the server half is used.
/// </summary>
public sealed partial class KsChainProbeSystem : EntitySystem
{
    /// <summary>
    ///     Every <see cref="ChainSegmentedEvent"/>, as (link it was raised on, link that broke).
    /// </summary>
    public readonly List<(EntityUid RaisedOn, EntityUid Broken)> Segmented = new();

    /// <summary>
    ///     Every <see cref="ChainInitiallyBrokenEvent"/>, as (edge it was raised on, link or edge that broke).
    /// </summary>
    public readonly List<(EntityUid RaisedOn, EntityUid Broken)> InitiallyBroken = new();

    /// <summary>
    ///     Every trigger that reached a <see cref="LinkedChainEndComponent"/>, as (end, key).
    /// </summary>
    public readonly List<(EntityUid End, string? Key)> EndTriggers = new();

    public void Clear()
    {
        Segmented.Clear();
        InitiallyBroken.Clear();
        EndTriggers.Clear();
    }

    [SubscribeLocalEvent]
    private void OnSegmented(Entity<ChainLinkComponent> entity, ref ChainSegmentedEvent args)
    {
        Segmented.Add((entity.Owner, args.BrokenEntity.Owner));
    }

    [SubscribeLocalEvent]
    private void OnInitiallyBroken(Entity<ChainEdgeComponent> entity, ref ChainInitiallyBrokenEvent args)
    {
        InitiallyBroken.Add((entity.Owner, args.BrokenUid));
    }

    [SubscribeLocalEvent]
    private void OnEndTriggered(Entity<LinkedChainEndComponent> entity, ref TriggerEvent args)
    {
        EndTriggers.Add((entity.Owner, args.Key));
    }
}
