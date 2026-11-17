using Content.Server.Administration;
using Content.Server._KS14.NPC.Debug;
using Content.Shared.Administration;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Toggles the hunt debug overlay for the invoking player - rooms, the ways into them, routes round them, what has
///         been searched and what has not, and every member's order - for every hunt, or one squad's, given a squad
///         or any NPC in it. See <see cref="NpcHuntDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class HuntDebugCommand : NpcScopedDebugCommand
{
    [Dependency] private NpcHuntDebugSystem _npcHuntDebugSystem = default!;

    public override string Command => "ks_huntdebug";

    protected override (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        return _npcHuntDebugSystem.Toggle(session, target);
    }
}
