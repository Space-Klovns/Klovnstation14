using System.Text;
using Content.Shared.Administration;
using Content.Server.Administration;
using Robust.Shared.Console;

namespace Content.Server._KS14.Procedural;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class KsProcgenSnapshotCommand : LocalizedEntityCommands
{
    [Dependency] private KsProcgenAreaSnapshotSystem _snapshotSystem = default!;
    public override string Command => "ksprocgen_snapshot";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !NetEntity.TryParse(args[0], out var gridNetEntity) ||
            !EntityManager.TryGetEntity(gridNetEntity, out var gridUid) || gridUid == null)
        {
            shell.WriteError(Loc.GetString("cmd-ksprocgen_snapshot-invalid-arguments"));
            return;
        }
        var snapshot = _snapshotSystem.Snapshot(gridUid.Value, args[1]);
        var exported = KsProcgenDiagnosticExport.SerializeArea(snapshot);
        if (exported.Status != KsProcgenDiagnosticExportStatus.Complete)
        {
            shell.WriteError(Loc.GetString("cmd-ksprocgen_snapshot-export-failed", ("reason", exported.Issue!.Code)));
            return;
        }
        shell.WriteLine(Encoding.UTF8.GetString(exported.Data));
    }
}
