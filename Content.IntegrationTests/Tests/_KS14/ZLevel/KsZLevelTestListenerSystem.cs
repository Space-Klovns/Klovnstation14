#nullable enable
using System.Collections.Generic;
using Content.IntegrationTests.Tests.Helpers;
using Content.Shared._KS14.ZLevel.Physics;
using Content.Shared.DeviceLinking.Events;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._KS14.ZLevel;

/// <summary>
///     Records z-level transit events, and can answer the two attempt events, for any entity carrying a
///         <see cref="TestListenerComponent"/>.
/// </summary>
/// <remarks>
///     Filtering on <see cref="TestListenerComponent"/> is what keeps this from leaking into other tests: an
///         entity has to opt in. The flags still have to be reset per test, because a pooled pair reuses the
///         same system instance.
/// </remarks>
public sealed class KsZLevelTestListenerSystem : EntitySystem
{
    /// <summary>
    ///     Answers <see cref="KsZLevelLandAttemptEvent"/> with no damage, whatever the impact speed.
    /// </summary>
    public bool VetoLandingDamage;

    /// <summary>
    ///     Answers <see cref="KsZLevelTransitMoveAttemptEvent"/> by letting the entity move itself anyway.
    /// </summary>
    public bool AllowMovementInTransit;

    /// <summary>
    ///     Sink ports that have been invoked on the listener, in the order they fired.
    /// </summary>
    public readonly List<string> SignalsReceived = [];

    public readonly List<KsZLevelLandEvent> Landings = [];
    public readonly List<KsZLevelChangedEvent> LevelChanges = [];
    public int TransitsStarted;
    public int TransitsEnded;

    public void Reset()
    {
        VetoLandingDamage = false;
        AllowMovementInTransit = false;
        SignalsReceived.Clear();
        Landings.Clear();
        LevelChanges.Clear();
        TransitsStarted = 0;
        TransitsEnded = 0;
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TestListenerComponent, KsZLevelTransitStartedEvent>(OnTransitStarted);
        SubscribeLocalEvent<TestListenerComponent, KsZLevelTransitEndedEvent>(OnTransitEnded);
        SubscribeLocalEvent<TestListenerComponent, KsZLevelChangedEvent>(OnZLevelChanged);
        SubscribeLocalEvent<TestListenerComponent, KsZLevelLandAttemptEvent>(OnLandAttempt);
        SubscribeLocalEvent<TestListenerComponent, KsZLevelLandEvent>(OnLand);
        SubscribeLocalEvent<TestListenerComponent, KsZLevelTransitMoveAttemptEvent>(OnMoveAttempt);
        SubscribeLocalEvent<TestListenerComponent, SignalReceivedEvent>(OnSignalReceived);
    }

    private void OnTransitStarted(Entity<TestListenerComponent> entity, ref KsZLevelTransitStartedEvent args)
    {
        TransitsStarted++;
    }

    private void OnTransitEnded(Entity<TestListenerComponent> entity, ref KsZLevelTransitEndedEvent args)
    {
        TransitsEnded++;
    }

    private void OnZLevelChanged(Entity<TestListenerComponent> entity, ref KsZLevelChangedEvent args)
    {
        LevelChanges.Add(args);
    }

    private void OnLandAttempt(Entity<TestListenerComponent> entity, ref KsZLevelLandAttemptEvent args)
    {
        if (VetoLandingDamage)
            args.Damaging = false;
    }

    private void OnLand(Entity<TestListenerComponent> entity, ref KsZLevelLandEvent args)
    {
        Landings.Add(args);
    }

    private void OnMoveAttempt(Entity<TestListenerComponent> entity, ref KsZLevelTransitMoveAttemptEvent args)
    {
        if (AllowMovementInTransit)
            args.Blocked = false;
    }

    private void OnSignalReceived(Entity<TestListenerComponent> entity, ref SignalReceivedEvent args)
    {
        SignalsReceived.Add(args.Port);
    }
}
