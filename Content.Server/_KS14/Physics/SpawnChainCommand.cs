using System.Numerics;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Physics;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._KS14.Physics;

[AdminCommand(AdminFlags.Fun)]
public sealed class SpawnChainCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly JointSystem _jointSystem = default!;

    public override string Command => "spawnchain";

    /// <summary>
    ///     Offset should have no X value lol
    /// </summary>
    private void ConnectTwo(EntityUid firstUid, EntityUid secondUid, Vector2 offset)
    {
        var joint = _jointSystem.CreateDistanceJoint(firstUid, secondUid, anchorA: offset, anchorB: -offset);

        joint.CollideConnected = false;
        joint.MinLength = offset.Y * 0.95f;
        joint.MaxLength = offset.Y;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteError(Loc.GetString("cmd-spawnchain-invalid-args"));
            return;
        }

        if (!_prototypeManager.HasIndex<EntityPrototype>(args[0]))
        {
            shell.WriteError(Loc.GetString("cmd-spawnchain-bad-proto", ("alleged", args[0])));
            return;
        }

        if (!int.TryParse(args[1], out var length))
        {
            shell.WriteError(Loc.GetString("cmd-spawnchain-bad-count", ("alleged", args[1])));
            return;
        }

        if (!float.TryParse(args[2], out var offsetY))
        {
            shell.WriteError(Loc.GetString("cmd-spawnchain-bad-offset", ("alleged", args[2])));
            return;
        }

        var offset = new Vector2(0f, offsetY);
        var coordinates = _entityManager.GetComponent<TransformComponent>(shell.Player?.AttachedEntity ?? EntityUid.Invalid).Coordinates;
        var lastUid = EntityUid.Invalid;

        for (var i = 0; i < length; i++)
        {
            var linkUid = _entityManager.SpawnAttachedTo(args[0], coordinates);
            if (lastUid == EntityUid.Invalid)
            {
                lastUid = linkUid;
                continue;
            }

            ConnectTwo(lastUid, linkUid, offset);
            lastUid = linkUid;
        }

    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 0 || args.Length > 3)
            return CompletionResult.Empty;

        if (args.Length == 1)
            return CompletionResult.FromHintOptions(CompletionHelper.PrototypeIdsLimited<EntityPrototype>(args[0], _prototypeManager), Loc.GetString("cmd-spawnchain-proto-completion"));

        if (args.Length == 2)
            return CompletionResult.FromHintOptions(Array.Empty<string>(), Loc.GetString("cmd-spawnchain-count-completion"));

        return CompletionResult.FromHintOptions(Array.Empty<string>(), Loc.GetString("cmd-spawnchain-offset-completion"));
    }
}
