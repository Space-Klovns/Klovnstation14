using Content.Server._KS14.Ordnance.TTV;
using Content.Shared.Administration.Logs;
using Content.Shared.Atmos.Components;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._KS14.Ordnance.TTV;

public abstract class SharedTTVSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;

    protected EntityQuery<GasTankComponent> GasTankQuery;
    protected EntityQuery<TTVCompatibleComponent> TTVCompatibleQuery;

    public override void Initialize()
    {
        base.Initialize();

        GasTankQuery = GetEntityQuery<GasTankComponent>();
        TTVCompatibleQuery = GetEntityQuery<TTVCompatibleComponent>();

        SubscribeLocalEvent<TTVComponent, UseInHandEvent>(OnTTVUse);
        SubscribeLocalEvent<TTVComponent, ExaminedEvent>(OnTTVExamine);
    }

    private void OnTTVExamine(Entity<TTVComponent> ttv, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("ttv-on-examine", ("open", ttv.Comp.Open)));
    }

    private void OnTTVUse(Entity<TTVComponent> ttv, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        var ttvComponent = ttv.Comp;
        _audioSystem.PlayPredicted(ttvComponent.ToggleSound, ttv, args.User);

        var open = !ttvComponent.Open;
        ttvComponent.Open = open;

        _adminLogger.Add(LogType.Explosion,
            LogImpact.High,
            $"{ToPrettyString(args.User):player} {(open ? "opened" : "closed")} TTV {ToPrettyString(ttv)}");

        args.Handled = true;

        if (open)
            OnTTVOpen(ttv);
    }

    protected virtual void OnTTVOpen(Entity<TTVComponent> ttv) { }
}
