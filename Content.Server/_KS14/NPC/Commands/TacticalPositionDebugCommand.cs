using Content.Server.Administration;
using Content.Server._KS14.NPC.Debug;
using Content.Server._KS14.NPC.Systems;
using Content.Shared.Administration;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.Commands;

/// <summary>
/// Toggles the tactical position debug overlay (candidate scores, chosen spot, live claims) for the
/// invoking player - either for every NPC using the query, or scoped to a single entity given by argument.
/// See <see cref="NpcTacticalPositionDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class TacticalPositionDebugCommand : NpcScopedDebugCommand
{
    [Dependency] private NpcTacticalPositionDebugSystem _npcTacticalPositionDebugSystem = default!;

    public override string Command => "ks_tacticalposdebug";

    protected override (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        return _npcTacticalPositionDebugSystem.Toggle(session, target);
    }
}
