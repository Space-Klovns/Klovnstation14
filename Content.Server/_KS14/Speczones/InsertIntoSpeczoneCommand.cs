// SPDX-FileCopyrightText: 2026 LaCumbiaDelCoronavirus
// SPDX-FileCopyrightText: 2026 github_actions[bot]
//
// SPDX-License-Identifier: MIT

// Licensed MIT because this was copied from core files

using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._KS14.Speczones;

[AdminCommand(AdminFlags.Debug)]
public sealed class InsertIntoSpeczoneCommand : LocalizedEntityCommands
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SpeczoneSystem _speczoneSystem = default!;

    public override string Command => "insertintospeczone";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 && args.Length != 2)
        {
            shell.WriteError(Loc.GetString(Loc.GetString("cmd-insertintospeczone-invalid-args")));
            return;
        }

        if (!EntityUid.TryParse(args[0], out var uid) ||
            !uid.IsValid() ||
            !EntityManager.EntityExists(uid))
        {
            shell.WriteError(Loc.GetString("cmd-insertintospeczone-invalid-uid"));
            return;
        }

        args.TryGetValue(1, out var speczoneIdOrNull);
        _speczoneSystem.TryInsertIntoSpeczone(uid, speczoneIdOrNull, out _);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
            return CompletionResult.Empty;

        return CompletionResult.FromHintOptions(
            _prototypeManager.EnumeratePrototypes<SpeczonePrototype>().Select(sz => sz.ID).Order(),
            Loc.GetString("cmd-insertintospeczone-speczone-completion"));
    }
}
