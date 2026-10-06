using Content.Shared._KS14.PopupLocalization; // KS14: deferred popup localization
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Content.Shared.Singularity.Components;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Shared.Singularity.EntitySystems;

public abstract partial class SharedEmitterSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmitterComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<EmitterComponent, GetVerbsEvent<Verb>>(OnGetVerb);
    }

    private void OnGetVerb(Entity<EmitterComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !args.CanComplexInteract || args.Hands == null)
            return;

        if (TryComp<LockComponent>(ent.Owner, out var lockComp) && lockComp.Locked)
            return;

        if (ent.Comp.SelectableTypes.Count < 2)
            return;

        var userUid = args.User; // KS14: retain the popup recipient for the verb callback
        foreach (var type in ent.Comp.SelectableTypes)
        {
            var proto = ProtoMan.Index(type);

            var v = new Verb
            {
                Priority = 1,
                Category = VerbCategory.SelectType,
                Text = _contentLocalizationManager.GetLocalizedPrototypeName(proto), // KS14: active client culture
                Disabled = type == ent.Comp.BoltType,
                Impact = LogImpact.Medium,
                DoContactInteraction = true,
                Act = () =>
                {
                    ent.Comp.BoltType = type;
                    Dirty(ent);
                    _popup.PopupClient(KsPopupMessage.Create /* KS14: localize popups on the recipient */("emitter-component-type-set", ("type", new KsPopupPrototypeName(proto.ID) /* KS14: defer selected prototype */)), ent.Owner, userUid);
                },
            };
            args.Verbs.Add(v);
        }
    }

    private void OnExamined(Entity<EmitterComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.SelectableTypes.Count < 2)
            return;

        var proto = ProtoMan.Index(ent.Comp.BoltType);
        args.PushMarkup(Loc.GetString("emitter-component-current-type", ("type", _contentLocalizationManager.GetLocalizedPrototypeName(proto) /* KS14: active response culture */)));
    }
}
