using Content.Server.NPC.HTN;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.Debug;

/// <summary>
///     A debug overlay toggle that takes an optional NPC to scope it to. The subclass names the command, carries the
///         admin attribute and forwards <see cref="Toggle"/> to its debug system; the messages are the loc keys
///         <c>cmd-{Command}-no-session</c>, <c>-invalid-entity</c>, <c>-disabled</c>, <c>-enabled-single</c>,
///         <c>-enabled-all</c> and <c>-entity-hint</c>.
/// </summary>
public abstract class NpcScopedDebugCommand : LocalizedEntityCommands
{
    protected abstract (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target);

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString($"cmd-{Command}-no-session"));
            return;
        }

        if (args.Length > 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        EntityUid? target = null;

        if (args.Length == 1)
        {
            if (!TryParseEntityArg(shell, EntityManager, Loc, args[0], $"cmd-{Command}-invalid-entity", out var targetUid))
                return;

            target = targetUid;
        }

        var (enabled, resultTarget) = Toggle(player, target);

        if (!enabled)
        {
            shell.WriteLine(Loc.GetString($"cmd-{Command}-disabled"));
            return;
        }

        shell.WriteLine(resultTarget is { } trackedUid
            ? Loc.GetString($"cmd-{Command}-enabled-single", ("entity", EntityManager.ToPrettyString(trackedUid)))
            : Loc.GetString($"cmd-{Command}-enabled-all"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            CompletionHelper.Components<HTNComponent>(args[0], EntityManager),
            Loc.GetString($"cmd-{Command}-entity-hint"));
    }

    /// <summary>
    ///     Parses a net entity argument to a live entity, writing <paramref name="invalidKey"/> (given the argument as
    ///         "entity") as an error if it is not one.
    /// </summary>
    public static bool TryParseEntityArg(IConsoleShell shell, IEntityManager entityManager, ILocalizationManager loc, string arg, string invalidKey, out EntityUid uid)
    {
        if (NetEntity.TryParse(arg, out var netEntity) && entityManager.TryGetEntity(netEntity, out var found))
        {
            uid = found.Value;
            return true;
        }

        shell.WriteError(loc.GetString(invalidKey, ("entity", arg)));
        uid = default;
        return false;
    }
}
