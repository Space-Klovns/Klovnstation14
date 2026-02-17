// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MPL-2.0

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Physics;
using Robust.Shared.Console;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Reflection;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Physics;

[AdminCommand(AdminFlags.Fun | AdminFlags.Debug)]
public sealed class AddJointCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IReflectionManager _reflectionManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly JointSystem _jointSystem = default!;

    private bool _areJointsRecognised = false;

    /// <summary>
    ///     Names of every type of every joint which there is.
    ///         Don't hold a reference to this because of LINQ.
    /// </summary>
    private List<string> _jointNames = [];

    public override string Command => "addjoint";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 3 && args.Length != 4)
        {
            shell.WriteError(Loc.GetString("cmd-addjoint-invalid-args"));
            return;
        }

        if (!TryExistingUid(args[0], out var firstUid, out var firstNetId, shell))
            return;

        if (!TryExistingUid(args[1], out var secondUid, out _, shell))
            return;

        var collideConnected = false; // default to false
        if (args.TryGetValue(3, out var boolArg) &&
            !bool.TryParse(boolArg, out collideConnected))
        {
            shell.WriteError(Loc.GetString("cmd-addjoint-bad-bool", ("alleged", boolArg)));
            return;
        }

        var jointId = $"ensured-entity-joint-{_gameTiming.CurTick.Value + firstNetId.Id}";
        var jointArg = args[2];

        Joint joint;
        switch (jointArg)
        {
            case "DistanceJoint":
                joint = _jointSystem.CreateDistanceJoint(firstUid.Value, secondUid.Value, id: jointId);
                break;
            case "MouseJoint":
                joint = _jointSystem.CreateMouseJoint(firstUid.Value, secondUid.Value, id: jointId);
                break;
            case "PrismaticJoint":
                joint = _jointSystem.CreatePrismaticJoint(firstUid.Value, secondUid.Value, id: jointId);
                break;
            case "RevoluteJoint":
                joint = _jointSystem.CreateRevoluteJoint(firstUid.Value, secondUid.Value, id: jointId);
                break;
            case "WeldJoint":
                joint = _jointSystem.CreateWeldJoint(firstUid.Value, secondUid.Value, id: jointId);
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-addjoint-bad-joint", ("name", jointArg)));
                return;
        }

        joint.CollideConnected = collideConnected;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 0 || args.Length > 4)
            return CompletionResult.Empty;

        if (args.Length == 1 || args.Length == 2)
        {
            var i = args.Length - 1;
            return CompletionResult.FromHintOptions(CompletionHelper.Components<TransformComponent>(args[i]), Loc.GetString("cmd-addjoint-uid-completion"));
        }

        if (args.Length == 3)
        {
            SetupTypesIfNecessary();
            return CompletionResult.FromHintOptions(_jointNames, Loc.GetString("cmd-addjoint-joint-completion"));
        }

        return CompletionResult.FromHintOptions(CompletionHelper.Booleans, Loc.GetString("cmd-addjoint-bool-completion"));
    }

    private bool TryExistingUid(string arg, [NotNullWhen(true)] out EntityUid? uid, [MaybeNullWhen(false)] out NetEntity netId, in IConsoleShell shell)
    {
        if (NetEntity.TryParse(arg, out netId) &&
            _entityManager.TryGetEntity(netId, out uid))
        {
            if (!_entityManager.EntityExists(uid))
            {
                shell.WriteError(Loc.GetString("cmd-addjoint-fake-uid", ("uid", uid)));
                return false;
            }
        }
        else
        {
            shell.WriteError(Loc.GetString("cmd-addjoint-bad-uid", ("alleged", arg)));

            uid = null;
            return false;
        }

        return true;
    }

    private void SetupTypesIfNecessary()
    {
        if (_areJointsRecognised)
            return;

        _areJointsRecognised = true;

        var jointTypes = _reflectionManager.GetAllChildren<Joint>(inclusive: false);
        foreach (var type in jointTypes)
            _jointNames.Add(type.Name);

        _jointNames = [.. _jointNames.Order()];
    }
}
