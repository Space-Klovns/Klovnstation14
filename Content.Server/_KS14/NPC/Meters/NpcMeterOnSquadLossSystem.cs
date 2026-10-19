using Content.Server._KS14.NPC.Squad;
using Content.Shared.Mobs;

namespace Content.Server._KS14.NPC.Meters;

/// <summary>
///     Raises <see cref="NpcMeterOnSquadLossComponent"/> meters on a squad's survivors when one of them goes down.
/// </summary>
public sealed partial class NpcMeterOnSquadLossSystem : EntitySystem
{
    [Dependency] private NpcMeterSystem _npcMeterSystem = default!;

    [Dependency] private EntityQuery<NpcSquadComponent> _squadQuery = default!;
    [Dependency] private EntityQuery<NpcMeterOnSquadLossComponent> _onSquadLossQuery = default!;

    [SubscribeLocalEvent]
    private void OnMemberDowned(ref NpcSquadMemberDownedEvent args)
    {
        if (!_squadQuery.TryComp(args.SquadUid, out var squadComponent))
            return;

        foreach (var memberUid in squadComponent.Members)
        {
            if (memberUid == args.MemberUid || !_onSquadLossQuery.TryComp(memberUid, out var onSquadLossComponent))
                continue;

            var amount = args.NewMobState == MobState.Dead ? onSquadLossComponent.Dead : onSquadLossComponent.Critical;
            _npcMeterSystem.Add(memberUid, onSquadLossComponent.Meter, amount);
        }
    }
}
