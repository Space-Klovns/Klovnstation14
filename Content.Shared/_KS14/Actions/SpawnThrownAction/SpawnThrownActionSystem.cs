using Content.Shared.Throwing;
using Robust.Shared.Network;

namespace Content.Shared._KS14.Actions.SpawnThrownAction;

/// <summary>
///     Spawns an entity on the performer and throws it at the action's target.
/// </summary>
public sealed partial class SpawnThrownActionSystem : EntitySystem
{
    [Dependency] private INetManager _netManager = default!;
    [Dependency] private ThrowingSystem _throwingSystem = default!;

    [SubscribeLocalEvent]
    private void OnSpawnThrownAction(SpawnThrownActionEvent args)
    {
        // Not predicted: what gets thrown is gameplay (a bomb, say), so the server's copy is the only one that may exist.
        //      A kept predicted copy would be a second, inert bomb in the wrong place on the performer's screen.
        if (_netManager.IsClient)
            return;

        var uid = SpawnAttachedTo(args.Prototype, Transform(args.Performer).Coordinates);
        _throwingSystem.TryThrow(uid, args.Target, baseThrowSpeed: args.ThrowSpeed, user: args.Performer);
    }
}
