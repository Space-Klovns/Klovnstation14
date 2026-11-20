#nullable enable
using Content.Server._KS14.GhostRespawn;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._KS14.GhostRespawn;

/// <summary>
///     A <see cref="KsCanGhostRespawnEvent"/> gate for tests: refuses every respawn while <see cref="DenyReason"/> is
///         set. The test assembly's systems are registered on both sides; only the server half is used.
/// </summary>
public sealed partial class KsGhostRespawnGateProbeSystem : EntitySystem
{
    public LocId? DenyReason;

    [SubscribeLocalEvent]
    private void OnCanGhostRespawn(ref KsCanGhostRespawnEvent args)
    {
        if (DenyReason is { } reason)
            args.Deny(reason);
    }
}
