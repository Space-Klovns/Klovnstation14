using System.Linq;
using Content.Server.Administration;
using Content.Shared._KS14.SupplyPod;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.SupplyPod.Commands;

/// <summary>
///     tgstation's podlauncher in reverse: drops a pod onto the ground, unlaunched and unlocked, for
///         <see cref="LaunchSupplyPodCommand"/> to send back up later.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class SpawnUnlaunchedSupplyPodCommand : LocalizedEntityCommands
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SupplyPodSystem _supplyPodSystem = default!;

    public override string Command => "spawnunlaunchedpod";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is not (1 or 2))
        {
            shell.WriteError(Loc.GetString("cmd-spawnunlaunchedpod-invalid-args"));
            return;
        }

        if (!_prototypeManager.TryIndex<EntityPrototype>(args[0], out var entityPrototype)
            || !entityPrototype.Components.ContainsKey(SupplyPodComponentName))
        {
            shell.WriteError(Loc.GetString("cmd-spawnunlaunchedpod-invalid-proto", ("proto", args[0])));
            return;
        }

        // No target means 'at my feet', which is how these get used in practice.
        var locationUid = shell.Player?.AttachedEntity;
        if (args.Length == 2)
        {
            if (!NetEntity.TryParse(args[1], out var locationNetEntity)
                || !_entityManager.TryGetEntity(locationNetEntity, out locationUid))
            {
                shell.WriteError(Loc.GetString("cmd-spawnunlaunchedpod-invalid-uid", ("uid", args[1])));
                return;
            }
        }

        if (locationUid is not { } targetUid || !_entityManager.EntityExists(targetUid))
        {
            shell.WriteError(Loc.GetString("cmd-spawnunlaunchedpod-no-location"));
            return;
        }

        var podUid = _supplyPodSystem.SpawnUnlaunchedPod(args[0], _entityManager.GetComponent<TransformComponent>(targetUid).Coordinates);

        shell.WriteLine(Loc.GetString(
            "cmd-spawnunlaunchedpod-spawned",
            ("uid", _entityManager.GetNetEntity(podUid))
        ));
    }

    private string SupplyPodComponentName => _componentFactory.GetComponentName(typeof(SupplyPodComponent));

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                _prototypeManager.EnumeratePrototypes<EntityPrototype>()
                    .Where(entityPrototype => !entityPrototype.Abstract && entityPrototype.Components.ContainsKey(SupplyPodComponentName))
                    .Select(entityPrototype => entityPrototype.ID)
                    .Order(),
                Loc.GetString("cmd-spawnunlaunchedpod-proto-completion")),
            2 => CompletionResult.FromHintOptions(
                CompletionHelper.NetEntities(args[1], _entityManager),
                Loc.GetString("cmd-spawnunlaunchedpod-uid-completion")),
            _ => CompletionResult.Empty
        };
    }
}
