using Content.Server.Administration;
using Content.Shared._KS14.SupplyPod;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;

namespace Content.Server._KS14.SupplyPod.Commands;

/// <summary>
///     Sends a pod spawned by <see cref="SpawnUnlaunchedSupplyPodCommand"/> back up, cargo and all.
///         It comes down again on the dropoff, or on the tile it left from when none is given.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class LaunchSupplyPodCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private SupplyPodSystem _supplyPodSystem = default!;

    public override string Command => "launchsupplypod";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is not (1 or 2))
        {
            shell.WriteError(Loc.GetString("cmd-launchsupplypod-invalid-args"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var podNetEntity)
            || !_entityManager.TryGetEntity(podNetEntity, out var podUid))
        {
            shell.WriteError(Loc.GetString("cmd-launchsupplypod-invalid-uid", ("uid", args[0])));
            return;
        }

        EntityCoordinates? dropoffCoordinates = null;
        if (args.Length == 2)
        {
            if (!NetEntity.TryParse(args[1], out var dropoffNetEntity)
                || !_entityManager.TryGetEntity(dropoffNetEntity, out var dropoffUid))
            {
                shell.WriteError(Loc.GetString("cmd-launchsupplypod-invalid-uid", ("uid", args[1])));
                return;
            }

            dropoffCoordinates = _entityManager.GetComponent<TransformComponent>(dropoffUid.Value).Coordinates;
        }

        if (!_supplyPodSystem.TryLaunchPod(podUid.Value, dropoffCoordinates))
        {
            shell.WriteError(Loc.GetString("cmd-launchsupplypod-not-launchable", ("uid", args[0])));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-launchsupplypod-launched", ("uid", args[0])));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<UnlaunchedSupplyPodComponent>(args[0], _entityManager),
                Loc.GetString("cmd-launchsupplypod-pod-completion")),
            2 => CompletionResult.FromHintOptions(
                CompletionHelper.NetEntities(args[1], _entityManager),
                Loc.GetString("cmd-launchsupplypod-dropoff-completion")),
            _ => CompletionResult.Empty
        };
    }
}
