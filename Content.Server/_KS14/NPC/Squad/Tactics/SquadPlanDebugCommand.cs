using Content.Server.Administration;
using Content.Server._KS14.NPC.Debug;
using Content.Shared.Administration;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.Squad.Tactics;

/// <summary>
///     Toggles the squad plan debug overlay for the invoking player - what a squad knows, what it means to do about a hostile
///         it cannot get at, and how far each member's shot gets - for every squad, or one, given a squad or any NPC in it.
///         See <see cref="NpcSquadPlanDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class SquadPlanDebugCommand : NpcScopedDebugCommand
{
    [Dependency] private NpcSquadPlanDebugSystem _npcSquadPlanDebugSystem = default!;

    public override string Command => "ks_squadplandebug";

    protected override (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        return _npcSquadPlanDebugSystem.Toggle(session, target);
    }
}
