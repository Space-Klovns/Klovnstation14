using Content.Shared.Interaction;
using System.Numerics;
using Content.Server._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Map;

namespace Content.Server._KS14.Cultivation.Systems;

public sealed partial class LegendarySystem
{
    [Dependency] private JianghuSystem _jianghuSystem = default!;

    #region Ganjiang and Moye

    /// <summary>
    ///     Is the other sword of the pair in this person's hands, or in the hands of someone right beside them?
    /// </summary>
    public bool Paired(Entity<TwinSwordComponent> entity, EntityUid userUid)
    {
        return PartnerHolder(entity, userUid) != null;
    }

    private EntityUid? PartnerHolder(Entity<TwinSwordComponent> entity, EntityUid userUid)
    {
        if (HoldsPartner(entity, userUid))
            return userUid;

        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(Transform(userUid).Coordinates, 1.5f, _mobs);
        foreach (var beside in _mobs)
        {
            if (beside.Owner != userUid && _mobStateSystem.IsAlive(beside.Owner) && HoldsPartner(entity, beside.Owner))
                return beside.Owner;
        }

        return null;
    }

    private bool HoldsPartner(Entity<TwinSwordComponent> entity, EntityUid holderUid)
    {
        foreach (var heldUid in _handsSystem.EnumerateHeld(holderUid))
        {
            if (heldUid != entity.Owner && MetaData(heldUid).EntityPrototype?.ID == entity.Comp.Partner.Id)
                return true;
        }

        return false;
    }

    private EntityUid? FindPartner(Entity<TwinSwordComponent> entity)
    {
        var query = EntityQueryEnumerator<TwinSwordComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out _, out var metaData))
        {
            if (metaData.EntityPrototype?.ID == entity.Comp.Partner.Id)
                return uid;
        }

        return null;
    }

    /// <summary>
    ///     Held together they unleash the Twin Dragon Sword Storm; apart, each tugs towards the other.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTwinSwordUsed(Entity<TwinSwordComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (PartnerHolder(entity, args.User) is not { } partnerHolderUid)
        {
            SensePartner(entity, args.User);
            return;
        }

        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextStorm)
        {
            _noticeSystem.Notify(args.User, Loc.GetString("legendary-twin-resting"));
            return;
        }

        StartStorm(entity, args.User);
        if (partnerHolderUid != args.User && FindPartner(entity) is { } partnerUid && TryComp<TwinSwordComponent>(partnerUid, out var partnerComponent))
            StartStorm((partnerUid, partnerComponent), partnerHolderUid);
    }

    private void StartStorm(Entity<TwinSwordComponent> entity, EntityUid spinnerUid)
    {
        var now = _gameTiming.CurTime;
        entity.Comp.NextStorm = now + entity.Comp.StormCooldown;
        entity.Comp.StormTicksLeft = entity.Comp.StormTicks;
        entity.Comp.NextStormTick = now;
        entity.Comp.Spinner = spinnerUid;
        Shout(spinnerUid, "legendary-twin-shout");
        _audioSystem.PlayPvs(RepulseSound, spinnerUid);
        _noticeSystem.Announce(spinnerUid, Loc.GetString("legendary-twin-storm", ("user", spinnerUid)), type: PopupType.LargeCaution);
    }

    private void UpdateTwinStorms(TimeSpan now)
    {
        var query = EntityQueryEnumerator<TwinSwordComponent>();
        while (query.MoveNext(out var uid, out var twinComponent))
        {
            if (twinComponent.StormTicksLeft <= 0 || now < twinComponent.NextStormTick || twinComponent.Spinner is not { } spinnerUid)
                continue;

            twinComponent.StormTicksLeft--;
            twinComponent.NextStormTick = now + twinComponent.StormInterval;
            if (TerminatingOrDeleted(spinnerUid) || !_mobStateSystem.IsAlive(spinnerUid) || !Paired((uid, twinComponent), spinnerUid))
            {
                twinComponent.StormTicksLeft = 0;
                continue;
            }

            _audioSystem.PlayPvs(BladeSound, spinnerUid);
            _visualsSystem.ArtOn(CultivationVisualsSystem.ArtSwordOrbit, spinnerUid);
            var centre = _transformSystem.GetMapCoordinates(spinnerUid);
            _mobs.Clear();
            _entityLookupSystem.GetEntitiesInRange(Transform(spinnerUid).Coordinates, 2f, _mobs);
            foreach (var victim in _mobs)
            {
                if (LegendaryHit(spinnerUid, victim.Owner, twinComponent.StormDamage, TimeSpan.Zero, uid))
                    _visualsSystem.Spark(victim.Owner, FxColour.Metal);
            }

            _bodyArtSystem.Smash(centre, 1.2f, 40f, wallTier: 0, spinnerUid);
        }
    }

    private void SensePartner(Entity<TwinSwordComponent> entity, EntityUid userUid)
    {
        if (FindPartner(entity) is not { } partnerUid)
        {
            _noticeSystem.Notify(userUid, Loc.GetString("legendary-twin-silent", ("item", entity.Owner)));
            return;
        }

        var here = _transformSystem.GetMapCoordinates(userUid);
        var there = _transformSystem.GetMapCoordinates(partnerUid);
        if (here.MapId != there.MapId)
        {
            _noticeSystem.Notify(userUid, Loc.GetString("legendary-twin-far", ("item", entity.Owner), ("partner", partnerUid)));
            return;
        }

        var offset = there.Position - here.Position;
        _visualsSystem.Wave(userUid, FxColour.Metal);
        _noticeSystem.Notify(userUid, Loc.GetString("legendary-twin-tug", ("item", entity.Owner), ("partner", partnerUid),
            ("distance", (int)MathF.Round(offset.Length())), ("direction", offset.ToWorldAngle().GetDir().ToString().ToLowerInvariant()),
            ("where", _noticeSystem.Where(partnerUid))), PopupType.Medium);
    }

    [SubscribeLocalEvent]
    private void OnTwinSwordEquipped(Entity<TwinSwordComponent> entity, ref GotEquippedHandEvent args)
    {
        if (HoldsPartner(entity, args.User))
            _noticeSystem.Notify(args.User, Loc.GetString("legendary-twin-reunited"), PopupType.Medium);
    }

    #endregion

    #region Heaven Reliant Sword

    /// <summary>
    ///     The Heaven-Cleaving Stroke: a blade of pale light fourteen tiles long and three wide.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnHeavenCleavingStroke(Entity<HeavenReliantComponent> entity, ref HeavenCleavingStrokeActionEvent args)
    {
        if (args.Handled)
            return;

        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextCleave)
        {
            _noticeSystem.Notify(args.Performer, Loc.GetString("legendary-reliant-gathering"));
            return;
        }

        var direction = _transformSystem.ToMapCoordinates(args.Target).Position - _transformSystem.GetMapCoordinates(args.Performer).Position;
        if (direction.LengthSquared() < 0.01f)
            return;

        args.Handled = true;
        entity.Comp.NextCleave = now + entity.Comp.CleaveCooldown;
        Shout(args.Performer, "legendary-reliant-shout");
        _audioSystem.PlayPvs(BladeSound, args.Performer);
        _audioSystem.PlayPvs(RepulseSound, args.Performer);
        _visualsSystem.Distort(_transformSystem.GetMapCoordinates(args.Performer), FxDistortion.Push);
        LegendaryLine(args.Performer, entity, direction, 14, 1, 25f, 2);
    }

    #endregion

    #region Dragon Slaying Saber

    /// <summary>
    ///     "Whoever holds the saber rules the martial world." Taking it up makes you First Under Heaven.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSaberEquipped(Entity<DragonSaberComponent> entity, ref GotEquippedHandEvent args)
    {
        var now = _gameTiming.CurTime;
        if (now < entity.Comp.NextClaim)
            return;

        entity.Comp.NextClaim = now + TimeSpan.FromMinutes(3);
        _noticeSystem.Notify(args.User, Loc.GetString("legendary-saber-first"), PopupType.Large, alsoToChat: true);
        _jianghuSystem.ClaimFirstUnderHeaven(args.User, "jianghu-saber-claimed");
    }

    [SubscribeLocalEvent]
    private void OnSaberUsed(Entity<DragonSaberComponent> entity, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var now = _gameTiming.CurTime;
        if (now < entity.Comp.AwakeUntil)
            return;

        if (now < entity.Comp.NextWake)
        {
            _noticeSystem.Notify(args.User, Loc.GetString("legendary-saber-sleeps"));
            return;
        }

        entity.Comp.NextWake = now + entity.Comp.WakeCooldown;
        entity.Comp.AwakeUntil = now + entity.Comp.AwakeDuration;
        _visualsSystem.ArtOn(CultivationVisualsSystem.ArtGoldMotes, args.User);
        _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, args.User);
        _noticeSystem.Announce(args.User, Loc.GetString("legendary-saber-wakes", ("user", args.User)), type: PopupType.LargeCaution);
    }

    /// <summary>
    ///     A golden dragon erupts from the blade: everything within three tiles is flattened, walls included.
    /// </summary>
    private void DragonSlayingStrike(Entity<DragonSaberComponent> entity, EntityUid userUid, EntityUid victimUid)
    {
        var centre = _transformSystem.GetMapCoordinates(victimUid);
        Shout(userUid, "legendary-saber-shout");
        _visualsSystem.Play(CultivationVisualsSystem.GreatBellSound, centre);
        _visualsSystem.Play(CultivationVisualsSystem.MountainFallSound, centre);
        _visualsSystem.Distort(centre, FxDistortion.Roar);
        _visualsSystem.Art(CultivationVisualsSystem.ArtRoarGold, centre);
        _bodyArtSystem.Impact(centre, 3f, strength: 3f, crater: 2);
        _bodyArtSystem.Smash(centre, 1.5f, 2500f, wallTier: 2, userUid);
        _bodyArtSystem.Smash(centre, 3f, 150f, wallTier: 0, userUid);
        LegendaryHit(userUid, victimUid, 45f, TimeSpan.FromSeconds(3), entity);

        _mobs.Clear();
        _entityLookupSystem.GetEntitiesInRange(centre, 3f, _mobs);
        foreach (var bystander in _mobs)
        {
            if (bystander.Owner == userUid || bystander.Owner == victimUid)
                continue;

            if (LegendaryHit(userUid, bystander.Owner, 15f, TimeSpan.FromSeconds(1.5), entity))
                Hurl(bystander.Owner, victimUid, 3f);
        }

        _noticeSystem.Announce(userUid, Loc.GetString("legendary-saber-strike", ("user", userUid)), type: PopupType.LargeCaution);
    }

    /// <summary>
    ///     The legend: strike the saber with the sword and both shatter, revealing the Nine Yin Manual and the Wumu Yishu
    ///         hidden inside. Offered as a verb, since there's no taking it back.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSaberVerbs(Entity<DragonSaberComponent> entity, ref GetVerbsEvent<Verb> args)
    {
        // The sword in any hand: right-click the saber.
        if (!args.CanAccess || !args.CanInteract || HeldWith<HeavenReliantComponent>(args.User) is not { } swordUid)
            return;

        AddClashVerb(ref args, entity, swordUid);
    }

    [SubscribeLocalEvent]
    private void OnReliantVerbs(Entity<HeavenReliantComponent> entity, ref GetVerbsEvent<Verb> args)
    {
        // Or the saber in any hand: right-click the sword.
        if (!args.CanAccess || !args.CanInteract || HeldWith<DragonSaberComponent>(args.User) is not { } saberUid)
            return;

        AddClashVerb(ref args, saberUid, entity);
    }

    /// <summary>
    ///     Strike one blade with the other (click it with the other in hand): the first strike warns, a second within
    ///         five seconds clashes them.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSaberStruck(Entity<DragonSaberComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<HeavenReliantComponent>(args.Used))
            return;

        args.Handled = true;
        TryClash(entity, args.Used, args.User);
    }

    [SubscribeLocalEvent]
    private void OnReliantStruck(Entity<HeavenReliantComponent> entity, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<DragonSaberComponent>(args.Used, out var saber))
            return;

        args.Handled = true;
        TryClash((args.Used, saber), entity, args.User);
    }

    private void TryClash(Entity<DragonSaberComponent> saber, EntityUid swordUid, EntityUid userUid)
    {
        var now = _gameTiming.CurTime;
        if (now > saber.Comp.ClashArmedUntil)
        {
            saber.Comp.ClashArmedUntil = now + TimeSpan.FromSeconds(5);
            _visualsSystem.Play(CultivationVisualsSystem.TempleBellSound, saber);
            _noticeSystem.Notify(userUid, Loc.GetString("legendary-secret-warning"), PopupType.LargeCaution);
            return;
        }

        RevealSecret(saber, swordUid, userUid);
    }

    private void AddClashVerb(ref GetVerbsEvent<Verb> args, EntityUid saberUid, EntityUid swordUid)
    {
        var userUid = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("legendary-secret-verb"),
            Message = Loc.GetString("legendary-secret-verb-message"),
            ConfirmationPopup = true,
            Act = () => RevealSecret(saberUid, swordUid, userUid),
        });
    }

    private EntityUid? HeldWith<T>(EntityUid userUid) where T : IComponent
    {
        foreach (var heldUid in _handsSystem.EnumerateHeld(userUid))
        {
            if (HasComp<T>(heldUid))
                return heldUid;
        }

        return null;
    }

    public void RevealSecret(EntityUid saberUid, EntityUid swordUid, EntityUid userUid)
    {
        if (TerminatingOrDeleted(saberUid) || TerminatingOrDeleted(swordUid))
            return;

        var clash = Transform(saberUid).Coordinates;
        _visualsSystem.Play(CultivationVisualsSystem.GreatBellSound, saberUid);
        _visualsSystem.WaveAt(_transformSystem.ToMapCoordinates(clash), FxColour.Gold, big: true);
        Spawn("KsNineYinManual", clash);
        Spawn("KsWumuYishu", clash);
        _noticeSystem.Announce(userUid, Loc.GetString("legendary-secret-revealed", ("user", userUid)), type: PopupType.LargeCaution);
        QueueDel(saberUid);
        QueueDel(swordUid);
    }

    #endregion
}
