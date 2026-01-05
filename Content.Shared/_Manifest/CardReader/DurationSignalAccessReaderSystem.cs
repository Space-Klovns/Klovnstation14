using Content.Shared.Access.Systems;
using Content.Shared.DeviceLinking;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Random.Helpers;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared.MNET.CardReader;

public abstract class SharedDurationSignalAccessReaderSystem : EntitySystem
{
    [Dependency] protected readonly SharedAppearanceSystem AppearanceSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;
    [Dependency] private readonly SharedPopupSystem _sharedPopupSystem = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLinkSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] private readonly UseDelaySystem _useDelaySystem = default!;

    public const string ReaderUseDelayId = "signalAccessReader";

    private readonly HashSet<Entity<DurationSignalAccessReaderComponent>> _activeReaders = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DurationSignalAccessReaderComponent, ComponentInit>(OnReaderInit);
        SubscribeLocalEvent<DurationSignalAccessReaderComponent, ComponentRemove>(OnReaderRemove);

        SubscribeLocalEvent<DurationSignalAccessReaderComponent, StartCollideEvent>(OnReaderCollide);

        SubscribeLocalEvent<DurationSignalAccessReaderComponent, ActivateInWorldEvent>(OnReaderActivated);
        SubscribeLocalEvent<DurationSignalAccessReaderComponent, DurationSignalAccessReaderDoAfterEvent>(OnReaderFinishDoAfter);
    }

    public override void Update(float frameTime)
    {
        if (!_gameTiming.IsFirstTimePredicted)
            return;

        foreach (var (uid, component) in _activeReaders)
        {
            if (_gameTiming.CurTime < component.NextStateChange)
                continue;

            SetReaderState((uid, component), DurationSignalAccessReaderState.Off);
        }
    }

    // TODO: Fix appearance prediction
    public bool SetReaderState(Entity<DurationSignalAccessReaderComponent> reader, DurationSignalAccessReaderState state)
    {
        if (state == reader.Comp.CurrentState)
            return false;

        reader.Comp.CurrentState = state;
        AppearanceSystem.SetData(reader.Owner, DurationSignalAccessReaderVisuals.State, state);

        if (state == DurationSignalAccessReaderState.Fail ||
            state == DurationSignalAccessReaderState.Success)
        {
            _activeReaders.Add(reader);
            reader.Comp.NextStateChange = _gameTiming.CurTime + reader.Comp.StateChangeTime;
        }
        else
            _activeReaders.Remove(reader);

        DirtyFields(reader, reader.Comp, null, nameof(DurationSignalAccessReaderComponent.CurrentState), nameof(DurationSignalAccessReaderComponent.NextStateChange));
        return true;
    }

    private void OnReaderInit(Entity<DurationSignalAccessReaderComponent> reader, ref ComponentInit args)
    {
        var (uid, component) = reader;
        _deviceLinkSystem.EnsureSourcePorts(uid, component.FailurePort, component.SuccessPort);
    }

    private void OnReaderRemove(Entity<DurationSignalAccessReaderComponent> reader, ref ComponentRemove args)
    {
        _activeReaders.Remove(reader);
    }

    private void OnReaderCollide(Entity<DurationSignalAccessReaderComponent> reader, ref StartCollideEvent args)
    {
        if (!reader.Comp.BumpAccessible)
            return;

        if (TryComp<UseDelayComponent>(reader, out var useDelay) && !_useDelaySystem.TryResetDelay((reader, useDelay), true, ReaderUseDelayId))
            return;

        if (args.OtherBody.BodyStatus != BodyStatus.InAir)
            return;

        var collidingEntity = args.OtherEntity;
        if (_tagSystem.HasTag(collidingEntity, SharedDoorSystem.DoorBumpTag))
        {
            if (_accessReaderSystem.IsAllowed(collidingEntity, reader.Owner))
                ReaderSuccess(reader, collidingEntity);
            else
                ReaderFailed(reader, collidingEntity);
        }
    }

    private void OnReaderActivated(Entity<DurationSignalAccessReaderComponent> reader, ref ActivateInWorldEvent args)
    {
        if (!args.Complex || args.Handled)
            return;

        if (TryComp<UseDelayComponent>(reader, out var useDelay) && !_useDelaySystem.TryResetDelay((reader, useDelay), true, ReaderUseDelayId))
            return;

        args.Handled = _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, reader.Comp.InteractionLength, new DurationSignalAccessReaderDoAfterEvent(), reader.Owner)
        {
            BreakOnWeightlessMove = false,
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnReaderFinishDoAfter(Entity<DurationSignalAccessReaderComponent> reader, ref DurationSignalAccessReaderDoAfterEvent args)
    {
        if (args.Cancelled)
            return;

        // This is a reallyneat trick, thanks to whoever made this hashcodecombine implementation.
        var (readerUid, readerComponent) = reader;
        var seed = SharedRandomExtensions.HashCodeCombine(new[] { (int)_gameTiming.CurTick.Value, GetNetEntity(readerUid).Id }); // sandbox violation giveaway if you make it `[...] instead of `new() { ... }`

        // fumble if unlucky enough
        if (new System.Random(seed).Prob(readerComponent.RepeatChance))
        {
            // popup is only shown to user
            if (readerComponent.RepeatPopupSelf is { } selfRepeatLoc && _gameTiming.IsFirstTimePredicted)
            {
                if (readerComponent.RepeatPopupOthers is { } othersRepeatLoc)
                    _sharedPopupSystem.PopupPredicted(Loc.GetString(selfRepeatLoc), Loc.GetString(othersRepeatLoc, ("name", Identity.Entity(args.User, EntityManager))), reader, args.User, PopupType.SmallCaution);
                else
                    _sharedPopupSystem.PopupPredicted(Loc.GetString(selfRepeatLoc), reader, args.User, PopupType.SmallCaution);
            }

            args.Repeat = true;
            return;
        }

        if (!_accessReaderSystem.IsAllowed(args.User, readerUid))
        {
            ReaderFailed(reader, args.User);
            return;
        }

        ReaderSuccess(reader, args.User);
    }

    public virtual void ReaderFailed(Entity<DurationSignalAccessReaderComponent> reader, EntityUid user)
    {
        _audioSystem.PlayPredicted(reader.Comp.FailureSound, reader.Owner, user);
        SetReaderState(reader, DurationSignalAccessReaderState.Fail);
    }

    public virtual void ReaderSuccess(Entity<DurationSignalAccessReaderComponent> reader, EntityUid user)
    {
        _audioSystem.PlayPredicted(reader.Comp.SuccessSound, reader.Owner, user);
        SetReaderState(reader, DurationSignalAccessReaderState.Success);
    }
}
