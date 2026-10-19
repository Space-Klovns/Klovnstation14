using Content.Server.Administration;
using Content.Shared._KS14.ZLevel;
using Content.Shared._KS14.ZLevel.Elevators;
using Content.Shared.Administration;
using Robust.Server.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;

namespace Content.Server._KS14.ZLevel.Elevators;

/// <summary>
///     Turns an existing grid into a working elevator.
/// </summary>
/// <remarks>
///     This is the mapper's entry point, and it is a command rather than something baked into the map
///         because of how z-levels are authored: a map is drawn standalone and only joins a stack once the
///         round links it, so at save time an elevator has no idea what floors exist. Nothing has to be
///         known in advance - the floors are simply whatever the stack turns out to hold.
/// </remarks>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class ZLevelElevatorCommand : LocalizedEntityCommands
{
    [Dependency] private KsZLevelSystem _zLevelSystem = default!;
    [Dependency] private ZLevelElevatorSystem _elevatorSystem = default!;

    public override string Command => "zlevel_elevator";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is not (1 or 2))
        {
            shell.WriteError(Loc.GetString("cmd-zlevel_elevator-invalid-args"));
            return;
        }

        if (!EntityUid.TryParse(args[0], out var gridUid) ||
            !gridUid.IsValid() ||
            !EntityManager.EntityExists(gridUid) ||
            !EntityManager.HasComponent<MapGridComponent>(gridUid))
        {
            shell.WriteError(Loc.GetString("cmd-zlevel_elevator-not-a-grid", ("uid", args[0])));
            return;
        }

        var elevatorComponent = EntityManager.EnsureComponent<ZLevelElevatorComponent>(gridUid);
        if (args.Length == 2)
            _elevatorSystem.SetShaftId((gridUid, elevatorComponent), args[1]);

        // Reported rather than assumed, because "it made no floors" is the single most likely thing to have
        //      gone wrong: a grid whose map has not been linked into a stack yet is a lift with nowhere to
        //      go, and it looks exactly like a working one until somebody presses a button.
        if (!_elevatorSystem.TryGetElevatorZLevel(gridUid, out var zLevelEntity))
        {
            shell.WriteLine(Loc.GetString("cmd-zlevel_elevator-no-zlevel"));
            return;
        }

        var stackEntities = new List<Entity<KsZLevelComponent>>();
        _zLevelSystem.TryGetStack(zLevelEntity.Value.Owner, stackEntities);

        shell.WriteLine(Loc.GetString(
            "cmd-zlevel_elevator-success",
            ("floors", stackEntities.Count),
            ("floor", _zLevelSystem.GetStackIndex(zLevelEntity.Value.Owner) + 1)
        ));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<MapGridComponent>(args[0], EntityManager),
                Loc.GetString("cmd-zlevel_elevator-completion-grid")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-zlevel_elevator-completion-shaft")),
            _ => CompletionResult.Empty,
        };
    }
}

/// <summary>
///     Drops an elevator controller console wherever the admin running it is standing.
/// </summary>
/// <remarks>
///     A controller does not have to be on the elevator it drives - that is the whole point of it - so
///         placing one is otherwise a matter of spawning a console and hoping it resolves to the right
///         shaft. This puts one down and says which elevator it found.
/// </remarks>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class ZLevelElevatorControllerCommand : LocalizedEntityCommands
{
    [Dependency] private TransformSystem _transformSystem = default!;
    [Dependency] private ZLevelElevatorSystem _elevatorSystem = default!;

    /// <summary>
    ///     What gets spawned. Not a parameter: anything else would need its own
    ///     <see cref="ZLevelElevatorControllerComponent"/> anyway, and spawning that is what `spawn` is for.
    /// </summary>
    private const string ControllerPrototype = "KsComputerElevator";

    public override string Command => "zlevel_elevator_controller";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("cmd-zlevel_elevator_controller-invalid-args"));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } playerUid)
        {
            shell.WriteError(Loc.GetString("cmd-zlevel_elevator_controller-no-body"));
            return;
        }

        var controllerUid = EntityManager.SpawnEntity(
            ControllerPrototype,
            _transformSystem.GetMapCoordinates(playerUid)
        );

        if (args.Length == 1 &&
            EntityManager.TryGetComponent<ZLevelElevatorControllerComponent>(controllerUid, out var controllerComponent))
            _elevatorSystem.SetShaftId((controllerUid, controllerComponent), args[0]);

        var shaftId = args.Length == 1 ? args[0] : null;

        shell.WriteLine(_elevatorSystem.TryResolveElevator(controllerUid, shaftId, out var elevatorEntity)
            ? Loc.GetString("cmd-zlevel_elevator_controller-success", ("elevator", elevatorEntity.Value.Owner))
            : Loc.GetString("cmd-zlevel_elevator_controller-unbound"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHint(Loc.GetString("cmd-zlevel_elevator-completion-shaft"))
            : CompletionResult.Empty;
    }
}
