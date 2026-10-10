using Content.Server.NPC;
using Content.Server.NPC.Systems;
using Content.Shared._KS14.Silicons.Bots;
using Robust.Shared.Map;

namespace Content.Server._KS14.Silicons.Bots;

public sealed partial class BotWranglerSystem : SharedBotWranglerSystem
{
    [Dependency] private NPCSystem _npcSystem = default!;

    public override void TryMoveBot(EntityUid botUid, EntityCoordinates targetCoordinates)
    {
        _npcSystem.SetBlackboard(botUid, NPCBlackboard.FollowTarget, targetCoordinates);
    }
}
