using Content.Shared._KS14.NPC;

namespace Content.Client._KS14.NPC;

/// <summary>
///     Client side of the hunt debug overlay. Keeps the latest frame of each from
///         <see cref="Content.Server._KS14.NPC.Squad.Tactics.NpcHuntDebugSystem"/>, and adds or removes
///         <see cref="HuntDebugOverlay"/> as the server says.
/// </summary>
public sealed partial class HuntDebugSystem : NpcDebugFrameSystem<HuntDebugDataMessage, HuntDebugOverlay>
{
    protected override HuntDebugOverlay CreateOverlay()
    {
        return new HuntDebugOverlay();
    }

    [SubscribeNetworkEvent]
    private void OnState(HuntDebugStateMessage message)
    {
        HandleState(message.Enabled);
    }

    [SubscribeNetworkEvent]
    private void OnData(HuntDebugDataMessage message)
    {
        HandleData(message.Issuer, message);
    }

    [SubscribeNetworkEvent]
    private void OnEnded(HuntDebugEndedMessage message)
    {
        HandleEnded(message.Issuer);
    }
}
