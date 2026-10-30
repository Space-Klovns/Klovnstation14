using Content.Server.Nuke;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;
using Robust.Shared.Reflection;

namespace Content.IntegrationTests.Tests._KS14.Nuke;

/// <summary>
///     Counts <see cref="NukeExplodedEvent"/>s, which is how the rest of the game learns a nuke went off.
/// </summary>
/// <remarks>
///     Subscriptions are locked once the server has started, so this has to be loaded through
///         <c>LoadExtraSystemType</c> rather than created by a test.
/// </remarks>
[Reflect(false)]
public sealed partial class KsNukeExplodedListenerSystem : EntitySystem
{
    public int ExplodedCount;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NukeExplodedEvent>(OnNukeExploded);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnNukeExploded(NukeExplodedEvent args)
    {
        ExplodedCount++;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        ExplodedCount = 0;
    }
}
