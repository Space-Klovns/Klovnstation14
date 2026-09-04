using System.Linq;
using Content.Server.Administration;
using Content.Shared._KS14.Atmos.ChemicalFire;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Atmos.ChemicalFire;

/// <summary>
///     Chemfires are hidden from the spawn menu and are only meant to be created through
///         <see cref="SharedChemicalFireSystem.SpawnChemicalFire"/>, so this exists to exercise that path
///         (deduplication, grid caching and all) by hand.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class SpawnChemicalFireCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private ChemicalFireSystem _chemicalFireSystem = default!;

    public override string Command => "spawnchemfire";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-spawnchemfire-invalid-args"));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } playerUid)
        {
            shell.WriteError(Loc.GetString("cmd-spawnchemfire-no-entity"));
            return;
        }

        if (!_prototypeManager.HasIndex<EntityPrototype>(args[0]))
        {
            shell.WriteError(Loc.GetString("cmd-spawnchemfire-invalid-prototype", ("prototype", args[0])));
            return;
        }

        if (_chemicalFireSystem.SpawnChemicalFire(args[0], EntityManager.GetComponent<TransformComponent>(playerUid).Coordinates) is null)
            shell.WriteError(Loc.GetString("cmd-spawnchemfire-failed"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1)
            return CompletionResult.Empty;

        var chemicalFireComponentName = _componentFactory.GetComponentName<ChemicalFireComponent>();

        return CompletionResult.FromHintOptions(
            _prototypeManager.EnumeratePrototypes<EntityPrototype>()
                .Where(prototype => !prototype.Abstract && prototype.Components.ContainsKey(chemicalFireComponentName))
                .Select(prototype => prototype.ID)
                .Order(),
            Loc.GetString("cmd-spawnchemfire-prototype-completion"));
    }
}
