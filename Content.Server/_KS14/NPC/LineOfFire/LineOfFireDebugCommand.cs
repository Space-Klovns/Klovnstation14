using Content.Server.Administration;
using Content.Server._KS14.NPC.Debug;
using Content.Shared.Administration;
using Robust.Shared.Player;

namespace Content.Server._KS14.NPC.LineOfFire;

/// <summary>
///     Toggles the line of fire debug overlay for the invoking player: how far the shot of each NPC with a gun on a target
///         would get, and what stops it. See <see cref="NpcLineOfFireDebugSystem"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class LineOfFireDebugCommand : NpcScopedDebugCommand
{
    [Dependency] private NpcLineOfFireDebugSystem _npcLineOfFireDebugSystem = default!;

    public override string Command => "ks_lineoffiredebug";

    protected override (bool Enabled, EntityUid? Target) Toggle(ICommonSession session, EntityUid? target)
    {
        return _npcLineOfFireDebugSystem.Toggle(session, target);
    }
}
