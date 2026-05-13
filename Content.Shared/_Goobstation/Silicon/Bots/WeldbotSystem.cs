using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Emag.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Shared._Goobstation.Silicon.Bots;

/// <summary>
/// Handles emagging Weldbots
/// </summary>
public sealed class WeldbotSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public DamageSpecifier GetDamageAmount(WeldbotComponent comp)
    {
        return comp.DamageAmount;
    }

    public Dictionary<ProtoId<DamageGroupPrototype>, FixedPoint2> GetDamageAmountGroups(WeldbotComponent comp, IPrototypeManager prototypeManager)
    {
        return comp.DamageAmount.GetDamagePerGroup(prototypeManager);
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WeldbotComponent, GotEmaggedEvent>(OnEmagged);
    }

    private void OnEmagged(EntityUid uid, WeldbotComponent comp, ref GotEmaggedEvent args)
    {
        _audio.PlayPredicted(comp.EmagSparkSound, uid, args.UserUid);

        comp.IsEmagged = true;
        args.Handled = true;
    }
}
