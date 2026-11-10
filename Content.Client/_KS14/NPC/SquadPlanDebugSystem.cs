using Content.Shared._KS14.NPC;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Client side of the squad plan debug overlay. Keeps the latest frame of each from
///         <see cref="Content.Server._KS14.NPC.Squad.Tactics.NpcSquadPlanDebugSystem"/>, and adds or removes
///         <see cref="SquadPlanDebugOverlay"/> as the server says.
/// </summary>
public sealed partial class SquadPlanDebugSystem : NpcDebugFrameSystem<SquadPlanDebugDataMessage, SquadPlanDebugOverlay>
{
    protected override SquadPlanDebugOverlay CreateOverlay()
    {
        return new SquadPlanDebugOverlay();
    }

    [SubscribeNetworkEvent]
    private void OnState(SquadPlanDebugStateMessage message)
    {
        HandleState(message.Enabled);
    }

    [SubscribeNetworkEvent]
    private void OnData(SquadPlanDebugDataMessage message)
    {
        HandleData(message.Issuer, message);
    }

    [SubscribeNetworkEvent]
    private void OnEnded(SquadPlanDebugEndedMessage message)
    {
        HandleEnded(message.Issuer);
    }
}
