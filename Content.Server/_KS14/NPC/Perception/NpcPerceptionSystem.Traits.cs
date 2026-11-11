using Content.Server.Body.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Flash;

namespace Content.Server._KS14.NPC.Perception;

public sealed partial class NpcPerceptionSystem
{
    [Dependency] private GunSystem _gunSystem = default!;
    [Dependency] private InternalsSystem _internalsSystem = default!;
    [Dependency] private SharedFlashSystem _flashSystem = default!;

    [Dependency] private EntityQuery<BloodstreamComponent> _bloodstreamQuery = default!;

    /// <summary>
    ///     What can be made of <paramref name="targetUid"/> by looking at it: only for one in sight. Worked out once a
    ///         sighting, as it is lost, and not on every update: <see cref="SharedFlashSystem.CanFlash"/> raises an event.
    /// </summary>
    public NpcContactTraits GetTraits(EntityUid targetUid)
    {
        if (TerminatingOrDeleted(targetUid))
            return NpcContactTraits.None;

        var traits = NpcContactTraits.Seen;

        if (_gunSystem.TryGetGun(targetUid, out _))
            traits |= NpcContactTraits.Armed;

        if (_flashSystem.CanFlash(targetUid))
            traits |= NpcContactTraits.Flashable;

        if (_bloodstreamQuery.HasComp(targetUid) && !_internalsSystem.AreInternalsWorking(targetUid))
            traits |= NpcContactTraits.Breathes;

        if (_perceptionQuery.TryComp(targetUid, out var perceptionComponent) && perceptionComponent.SeesThroughSmoke)
            traits |= NpcContactTraits.SeesThroughSmoke;

        return traits;
    }
}
