using System.Globalization;
using Content.Server._KS14.NPC.Debug;
using Content.Server._KS14.NPC.Squad;
using Content.Server.Administration;
using Content.Server.NPC.HTN;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.NPC.Meters;

/// <summary>
///     Sets a meter - caution, say - on an NPC, and on everyone in its squad if it is in one, to see how they act at that
///         value without having to get them there. Values are clamped to the meter's range, and decay from the moment
///         they are set, as always.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class SetMeterCommand : LocalizedEntityCommands
{
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;
    [Dependency] private NpcSquadSystem _npcSquadSystem = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public override string Command => "ks_setmeter";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!NpcScopedDebugCommand.TryParseEntityArg(shell, EntityManager, Loc, args[0], "cmd-ks_setmeter-invalid-entity", out var targetUid))
            return;

        if (!_prototypeManager.TryIndex<NpcMeterPrototype>(args[1], out var meter))
        {
            shell.WriteError(Loc.GetString("cmd-ks_setmeter-invalid-meter", ("meter", args[1])));
            return;
        }

        if (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            shell.WriteError(Loc.GetString("cmd-ks_setmeter-invalid-value", ("value", args[2])));
            return;
        }

        value = Math.Clamp(value, 0f, meter.Max);

        if (_npcSquadSystem.TryGetSquad(targetUid, out var squadEntity))
        {
            foreach (var memberUid in squadEntity.Value.Comp.Members)
            {
                _npcMeterSystem.Set(memberUid, meter.ID, value);
            }

            shell.WriteLine(Loc.GetString("cmd-ks_setmeter-set-squad",
                ("meter", meter.ID),
                ("value", value),
                ("count", squadEntity.Value.Comp.Members.Count),
                ("entity", EntityManager.ToPrettyString(targetUid))));
            return;
        }

        _npcMeterSystem.Set(targetUid, meter.ID, value);
        shell.WriteLine(Loc.GetString("cmd-ks_setmeter-set-single",
            ("meter", meter.ID),
            ("value", value),
            ("entity", EntityManager.ToPrettyString(targetUid))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.Components<HTNComponent>(args[0], EntityManager),
                Loc.GetString("cmd-ks_setmeter-entity-hint")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<NpcMeterPrototype>(proto: _prototypeManager),
                Loc.GetString("cmd-ks_setmeter-meter-hint")),
            3 => CompletionResult.FromHint(Loc.GetString("cmd-ks_setmeter-value-hint")),
            _ => CompletionResult.Empty,
        };
    }
}
