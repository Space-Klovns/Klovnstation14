using System.Linq;
using System.Numerics;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._KS14.GridCopy;

/// <summary>
///     Copies a grid (e.g. a ship) and pastes a duplicate onto the same map, offset next to the original.
///     Made for iterating on grid/ship designs on a dev map. See <see cref="GridCopySystem"/> for the actual
///     copy logic and its limitations.
/// </summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class CopyGridCommand : LocalizedEntityCommands
{
    [Dependency] private GridCopySystem _gridCopy = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override string Command => "copygrid";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        // Valid forms (a position needs BOTH x and y):
        //   copygrid                              -> grid under you, default offset
        //   copygrid <uid|here>                   -> that grid, default offset
        //   copygrid <uid|here> <x> <y>           -> custom offset from the original
        //   copygrid <uid|here> <x> <y> <deg>     -> custom offset + rotation
        //   copygrid <uid|here> abs <x> <y>       -> paste at absolute map coordinates
        //   copygrid <uid|here> abs <x> <y> <deg> -> absolute coordinates + rotation
        var absolute = args.Length >= 2 && string.Equals(args[1], "abs", StringComparison.OrdinalIgnoreCase);
        if (absolute ? args.Length is < 4 or > 5 : args.Length is 2 or > 4)
        {
            shell.WriteError(Loc.GetString("cmd-copygrid-invalid-args"));
            return;
        }

        if (!TryResolveGrid(shell, args, out var grid))
            return;

        // The load path works in offsets from the original's position, so absolute
        // coordinates convert to relative against the grid's map-local origin. The
        // default puts the copy just east of the original so they don't overlap.
        var coordBase = absolute ? 2 : 1;
        Vector2 offset;
        if (args.Length > coordBase + 1)
        {
            if (!float.TryParse(args[coordBase], out var x))
            {
                shell.WriteError(Loc.GetString("cmd-copygrid-bad-float", ("value", args[coordBase])));
                return;
            }

            if (!float.TryParse(args[coordBase + 1], out var y))
            {
                shell.WriteError(Loc.GetString("cmd-copygrid-bad-float", ("value", args[coordBase + 1])));
                return;
            }

            offset = new Vector2(x, y);
            if (absolute)
                offset -= EntityManager.GetComponent<TransformComponent>(grid.Owner).LocalPosition;
        }
        else
        {
            var worldAabb = _transform.GetWorldMatrix(grid.Owner).TransformBox(grid.Comp.LocalAABB);
            offset = new Vector2(worldAabb.Width + 2f, 0f);
        }

        // Optional trailing arg, applied about the copy's own origin.
        Angle rot = default;
        if (args.Length > coordBase + 2)
        {
            if (!float.TryParse(args[coordBase + 2], out var deg))
            {
                shell.WriteError(Loc.GetString("cmd-copygrid-bad-float", ("value", args[coordBase + 2])));
                return;
            }

            rot = Angle.FromDegrees(deg);
        }

        if (!_gridCopy.TryCopyGrid(grid, offset, rot, out var copy, out var error))
        {
            shell.WriteError(error);
            return;
        }

        var copyUid = copy.Value.Owner;
        var copyXform = EntityManager.GetComponent<TransformComponent>(copyUid);
        var worldPos = _transform.GetWorldPosition(copyXform);
        shell.WriteLine(Loc.GetString("cmd-copygrid-success",
            ("from", EntityManager.ToPrettyString(grid.Owner)),
            ("to", EntityManager.ToPrettyString(copyUid)),
            ("map", copyXform.MapID.ToString()),
            ("x", $"{worldPos.X:0.0}"),
            ("y", $"{worldPos.Y:0.0}")));
    }

    /// <summary>
    ///     Resolves the source grid from the first argument: an explicit uid, the literal <c>here</c>, or (when no
    ///     args) the grid the caller is standing on.
    /// </summary>
    private bool TryResolveGrid(IConsoleShell shell, string[] args, out Entity<MapGridComponent> grid)
    {
        grid = default;
        EntityUid uid;

        if (args.Length == 0 || string.Equals(args[0], "here", StringComparison.OrdinalIgnoreCase))
        {
            if (shell.Player?.AttachedEntity is not { } player)
            {
                shell.WriteError(Loc.GetString("cmd-copygrid-no-player"));
                return false;
            }

            if (EntityManager.GetComponent<TransformComponent>(player).GridUid is not { } standingOn)
            {
                shell.WriteError(Loc.GetString("cmd-copygrid-not-on-grid"));
                return false;
            }

            uid = standingOn;
        }
        else
        {
            if (!NetEntity.TryParse(args[0], out var net) || !EntityManager.TryGetEntity(net, out var resolved))
            {
                shell.WriteError(Loc.GetString("cmd-copygrid-bad-uid", ("value", args[0])));
                return false;
            }

            uid = resolved.Value;
        }

        if (!EntityManager.TryGetComponent(uid, out MapGridComponent? gridComp))
        {
            shell.WriteError(Loc.GetString("cmd-copygrid-not-a-grid", ("uid", EntityManager.ToPrettyString(uid))));
            return false;
        }

        grid = (uid, gridComp);
        return true;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        // In absolute mode ('abs' as the second argument) the coordinate/rotation slots shift right by one.
        var absolute = args.Length >= 2 && string.Equals(args[1], "abs", StringComparison.OrdinalIgnoreCase);
        return (args.Length - (absolute ? 1 : 0)) switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<MapGridComponent>(args[0], EntityManager)
                    .Prepend(new CompletionOption("here", Loc.GetString("cmd-copygrid-here-hint"))),
                Loc.GetString("cmd-copygrid-grid-completion")),
            2 => absolute
                ? CompletionResult.FromHint(Loc.GetString("cmd-copygrid-absx-completion"))
                : CompletionResult.FromHintOptions(
                    new[] { new CompletionOption("abs", Loc.GetString("cmd-copygrid-abs-hint")) },
                    Loc.GetString("cmd-copygrid-offsetx-completion")),
            3 => CompletionResult.FromHint(Loc.GetString(absolute ? "cmd-copygrid-absy-completion" : "cmd-copygrid-offsety-completion")),
            4 => CompletionResult.FromHint(Loc.GetString("cmd-copygrid-rot-completion")),
            _ => CompletionResult.Empty,
        };
    }
}
