using System;
using System.Linq;
using System.Text;
using Content.Server._KS14.Packet.Components;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Server._KS14.Packet.Commands
{
    [AdminCommand(AdminFlags.Debug)]
    public sealed partial class AddReferenceCommand : LocalizedCommands
    {
        [Dependency] private EntityManager _entMan = default!;

        public override string Command => "addreference";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length < 2)
            {
                shell.WriteLine("You should provide EntityUid and reference name");
                return;
            }

            var uid = args[0];
            var name = args[1];


            if (!EntityUid.TryParse(uid, out var entityUid))
            {
                shell.WriteLine("Couldn't parse EntityUid");
                return;
            }

            var referenceComp = _entMan.EnsureComponent<PacketReferenceComponent>(entityUid);
            referenceComp.RefName = name;

            shell.WriteLine("Added reference to " + _entMan.MetaQuery.GetComponent(entityUid).EntityName);
        }
    }
}
