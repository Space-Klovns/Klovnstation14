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
/// <remarks>
///     Named for what it does rather than for spawning: re-running it on a tile that already holds the same
///         chemfire retunes and restarts that one instead of adding a second.
/// </remarks>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class EnsureChemicalFireCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private ChemicalFireSystem _chemicalFireSystem = default!;

    public override string Command => "ensurechemfire";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Loc.GetString("cmd-ensurechemfire-invalid-args"));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } playerUid)
        {
            shell.WriteError(Loc.GetString("cmd-ensurechemfire-no-entity"));
            return;
        }

        if (!_prototypeManager.HasIndex<EntityPrototype>(args[0]))
        {
            shell.WriteError(Loc.GetString("cmd-ensurechemfire-invalid-prototype", ("prototype", args[0])));
            return;
        }

        TimeSpan? duration = null;
        if (args.Length == 2)
        {
            if (!float.TryParse(args[1], out var durationSeconds) || durationSeconds <= 0f)
            {
                shell.WriteError(Loc.GetString("cmd-ensurechemfire-invalid-duration", ("duration", args[1])));
                return;
            }

            duration = TimeSpan.FromSeconds(durationSeconds);
        }

        var coordinates = EntityManager.GetComponent<TransformComponent>(playerUid).Coordinates;

        if (_chemicalFireSystem.SpawnChemicalFire(args[0], coordinates, duration) is null)
            shell.WriteError(Loc.GetString("cmd-ensurechemfire-failed"));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var chemicalFireComponentName = _componentFactory.GetComponentName<ChemicalFireComponent>();

            return CompletionResult.FromHintOptions(
                _prototypeManager.EnumeratePrototypes<EntityPrototype>()
                    .Where(prototype => !prototype.Abstract && prototype.Components.ContainsKey(chemicalFireComponentName))
                    .Select(prototype => prototype.ID)
                    .Order(),
                Loc.GetString("cmd-ensurechemfire-prototype-completion"));
        }

        if (args.Length == 2)
            return CompletionResult.FromHint(Loc.GetString("cmd-ensurechemfire-duration-completion"));

        return CompletionResult.Empty;
    }
}
