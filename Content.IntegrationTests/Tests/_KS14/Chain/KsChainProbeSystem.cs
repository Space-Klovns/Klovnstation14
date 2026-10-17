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
public sealed class KsChainProbeSystem : EntitySystem
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

    public override void Initialize()
    {
        // The generator is not imported into the test project, so these are subscribed by hand.
        SubscribeLocalEvent<ChainLinkComponent, ChainSegmentedEvent>(OnSegmented);
        SubscribeLocalEvent<ChainEdgeComponent, ChainInitiallyBrokenEvent>(OnInitiallyBroken);
        SubscribeLocalEvent<LinkedChainEndComponent, TriggerEvent>(OnEndTriggered);
    }

    public void Clear()
    {
        Segmented.Clear();
        InitiallyBroken.Clear();
        EndTriggers.Clear();
    }

    private void OnSegmented(EntityUid uid, ChainLinkComponent component, ref ChainSegmentedEvent args)
    {
        Segmented.Add((uid, args.BrokenEntity.Owner));
    }

    private void OnInitiallyBroken(EntityUid uid, ChainEdgeComponent component, ref ChainInitiallyBrokenEvent args)
    {
        InitiallyBroken.Add((uid, args.BrokenUid));
    }

    private void OnEndTriggered(EntityUid uid, LinkedChainEndComponent component, ref TriggerEvent args)
    {
        EndTriggers.Add((uid, args.Key));
    }
}
