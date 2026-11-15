using System.Diagnostics.CodeAnalysis;
using Content.Server._KS14.SupplyPod;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.SalvageTablet;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.SalvageTablet;

/// <summary>
///     Runs the salvage tablets and the accounts behind them: logging in, dealing, tracking and dropping tasks, counting
///         progress, and paying out mining points and reputation.
///
///     Whatever sees a task's objective happen reports it here with <see cref="Advance"/>.
/// </summary>
public sealed partial class KsSalvageTabletSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private UserInterfaceSystem _userInterfaceSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SupplyPodSystem _supplyPodSystem = default!;
    [Dependency] private KsMaterialRequestSystem _materialRequestSystem = default!;
    [Dependency] private EntityQuery<KsMinerAccountComponent> _accountQuery = default!;
    [Dependency] private EntityQuery<AccessComponent> _accessQuery = default!;
    [Dependency] private EntityQuery<IdCardComponent> _idCardQuery = default!;
    [Dependency] private EntityQuery<MobStateComponent> _mobStateQuery = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private EntityQuery<KsSalvageTabletScannedComponent> _scannedQuery = default!;
    [Dependency] private EntityQuery<KsSalvageTabletComponent> _tabletQuery = default!;

    private static readonly TimeSpan ExpiryCheckInterval = TimeSpan.FromSeconds(1);

    private TimeSpan _nextExpiryCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        if (curTime < _nextExpiryCheck)
            return;

        _nextExpiryCheck = curTime + ExpiryCheckInterval;

        var accountEnumerator = EntityQueryEnumerator<KsMinerAccountComponent>();
        while (accountEnumerator.MoveNext(out var cardUid, out var accountComponent))
        {
            // Only a tracked task has an end time.
            var failed = accountComponent.Tasks.RemoveAll(task => task.EndTime is { } endTime && curTime >= endTime);
            if (failed > 0)
                Fail((cardUid, accountComponent));

            // A slot a drop left empty is dealt again once the drop cooldown is over.
            var taskCount = accountComponent.Tasks.Count;
            if (failed > 0 || curTime >= accountComponent.NextDropTime)
                FillTasks((cardUid, accountComponent));

            if (failed > 0 || accountComponent.Tasks.Count != taskCount)
                UpdateUis((cardUid, accountComponent));
        }
    }

    /// <summary>
    ///     Logs the tablet in with the ID the user carries. The ID gets an account if it has none.
    /// </summary>
    public bool TryLogin(Entity<KsSalvageTabletComponent> tabletEntity, EntityUid userUid)
    {
        if (!TryFindIdCard(userUid, out var idCardEntity))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-login-no-id"), tabletEntity.Owner, userUid);
            return false;
        }

        if (!_accessQuery.TryComp(idCardEntity.Owner, out var accessComponent) ||
            !accessComponent.Tags.Contains(tabletEntity.Comp.RequiredAccess))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-login-denied"), tabletEntity.Owner, userUid);
            return false;
        }

        var accountComponent = EnsureComp<KsMinerAccountComponent>(idCardEntity.Owner);
        accountComponent.RankLadder = tabletEntity.Comp.RankLadder;
        accountComponent.TaskSlots = tabletEntity.Comp.TaskSlots;
        FillTasks((idCardEntity.Owner, accountComponent));

        Unbind(tabletEntity);
        tabletEntity.Comp.BoundCard = idCardEntity.Owner;
        accountComponent.Tablets.Add(tabletEntity.Owner);
        Dirty(tabletEntity);
        UpdateUi(tabletEntity);
        return true;
    }

    /// <summary>
    ///     Takes the tablet off the account it is logged in with, on both sides.
    /// </summary>
    private void Unbind(Entity<KsSalvageTabletComponent> tabletEntity)
    {
        if (tabletEntity.Comp.BoundCard is { } cardUid && _accountQuery.TryComp(cardUid, out var accountComponent))
            accountComponent.Tablets.Remove(tabletEntity.Owner);

        tabletEntity.Comp.BoundCard = null;
    }

    /// <summary>
    ///     The ID the user wears or holds. The tablet is in their active hand, so an ID in the other one counts too.
    /// </summary>
    private bool TryFindIdCard(EntityUid userUid, out Entity<IdCardComponent> idCardEntity)
    {
        if (_idCardSystem.TryFindIdCard(userUid, out idCardEntity))
            return true;

        foreach (var heldUid in _handsSystem.EnumerateHeld(userUid))
        {
            if (_idCardSystem.TryGetIdCard(heldUid, out idCardEntity))
                return true;
        }

        return false;
    }

    public void Logout(Entity<KsSalvageTabletComponent> tabletEntity)
    {
        Unbind(tabletEntity);
        Dirty(tabletEntity);
        UpdateUi(tabletEntity);
    }

    /// <summary>
    ///     Takes a task on: from now it counts progress, and its time runs. An account tracks only so many at once,
    ///         and a tracked task stays tracked until it is finished, failed or dropped.
    /// </summary>
    public bool TryTrackTask(Entity<KsSalvageTabletComponent> tabletEntity, ProtoId<KsSalvageTaskPrototype> taskId)
    {
        if (!TryGetAccount(tabletEntity, out var accountEntity) ||
            !TryGetTask(accountEntity, taskId, out var task) ||
            task.Tracked ||
            !ProtoMan.TryIndex(taskId, out var taskPrototype))
            return false;

        var tracked = 0;
        foreach (var activeTask in accountEntity.Comp.Tasks)
        {
            if (activeTask.Tracked)
                tracked++;
        }

        if (tracked >= tabletEntity.Comp.MaxTrackedTasks)
            return false;

        task.Tracked = true;
        task.EndTime = _gameTiming.CurTime + taskPrototype.Duration;
        UpdateUis(accountEntity);
        return true;
    }

    /// <summary>
    ///     Throws a task away, tracked or not, if the account's drop cooldown is over. Another task takes its place if
    ///         the account's rank is offered one it isn't already doing; otherwise the slot stays empty until the cooldown
    ///         is over.
    /// </summary>
    /// <param name="userUid">Who is told why a drop was refused, if anyone.</param>
    public bool TryDropTask(Entity<KsSalvageTabletComponent> tabletEntity, ProtoId<KsSalvageTaskPrototype> taskId, EntityUid? userUid = null)
    {
        if (!TryGetAccount(tabletEntity, out var accountEntity) || !TryGetTask(accountEntity, taskId, out var task))
            return false;

        if (_gameTiming.CurTime < accountEntity.Comp.NextDropTime)
        {
            if (userUid != null)
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-task-drop-refused"), tabletEntity.Owner, userUid.Value);
            return false;
        }

        // The old task stays in the list while the new one is picked, so the drop never deals the same task back.
        var index = accountEntity.Comp.Tasks.IndexOf(task);
        if (PickTask(accountEntity) is { } replacement)
            accountEntity.Comp.Tasks[index] = replacement;
        else
            accountEntity.Comp.Tasks.RemoveAt(index);

        accountEntity.Comp.NextDropTime = _gameTiming.CurTime + tabletEntity.Comp.DropCooldown;
        UpdateUis(accountEntity);
        return true;
    }

    private static bool TryGetTask(Entity<KsMinerAccountComponent> accountEntity, ProtoId<KsSalvageTaskPrototype> taskId, [NotNullWhen(true)] out KsSalvageActiveTask? task)
    {
        foreach (var activeTask in accountEntity.Comp.Tasks)
        {
            if (activeTask.Task != taskId)
                continue;

            task = activeTask;
            return true;
        }

        task = null;
        return false;
    }

    /// <summary>
    ///     Counts something done toward the account's tasks. Every tracked task whose objective matches moves forward,
    ///         and a finished one pays out and is replaced.
    /// </summary>
    /// <returns>Whether any task wanted it.</returns>
    public bool Advance<T>(Entity<KsMinerAccountComponent?> accountEntity, Predicate<T> matches, int amount = 1) where T : KsSalvageTaskObjective
    {
        if (amount <= 0 || !_accountQuery.Resolve(accountEntity.Owner, ref accountEntity.Comp, logMissing: false))
            return false;

        var advanced = false;
        var tasks = accountEntity.Comp.Tasks;
        for (var index = tasks.Count - 1; index >= 0; index--)
        {
            var task = tasks[index];
            if (!task.Tracked ||
                !ProtoMan.TryIndex(task.Task, out var taskPrototype) ||
                taskPrototype.Objective is not T objective ||
                !matches(objective))
                continue;

            advanced = true;
            task.Progress = Math.Min(task.Progress + amount, task.Amount);
            if (task.Progress < task.Amount)
                continue;

            tasks.RemoveAt(index);
            Complete((accountEntity.Owner, accountEntity.Comp), task, taskPrototype);
        }

        if (!advanced)
            return false;

        FillTasks((accountEntity.Owner, accountEntity.Comp));
        UpdateUis((accountEntity.Owner, accountEntity.Comp));
        return true;
    }

    /// <summary>
    ///     Pays mining points into an account.
    /// </summary>
    public void AddPoints(Entity<KsMinerAccountComponent> accountEntity, int points)
    {
        accountEntity.Comp.Points += points;
        UpdateUis(accountEntity);
    }

    /// <summary>
    ///     Buys the cart with the account's mining points and drops it next to the buyer in a pod. All of it or
    ///         nothing: one product above the account's rank, or a total above its points, refuses the order.
    /// </summary>
    public bool TryOrder(Entity<KsSalvageTabletComponent> tabletEntity, EntityUid buyerUid, Dictionary<ProtoId<KsSalvageProductPrototype>, int> cart)
    {
        if (!TryGetAccount(tabletEntity, out var accountEntity) || cart.Count == 0)
            return false;

        if (_gameTiming.CurTime < accountEntity.Comp.NextOrderTime)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-order-wait"), tabletEntity.Owner, buyerUid);
            return false;
        }

        var rank = GetRank(accountEntity);
        var total = 0;
        var contents = new List<EntProtoId>();
        foreach (var (productId, amount) in cart)
        {
            if (amount <= 0 ||
                amount > tabletEntity.Comp.MaxOrderAmount ||
                !ProtoMan.TryIndex(productId, out var productPrototype) ||
                productPrototype.MinRank > rank)
            {
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-order-refused"), tabletEntity.Owner, buyerUid);
                return false;
            }

            total += productPrototype.Cost * amount;
            for (var unit = 0; unit < amount; unit++)
            {
                contents.Add(productPrototype.Entity);
            }
        }

        if (total > accountEntity.Comp.Points)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-order-too-expensive"), tabletEntity.Owner, buyerUid);
            return false;
        }

        accountEntity.Comp.Points -= total;
        accountEntity.Comp.NextOrderTime = _gameTiming.CurTime + tabletEntity.Comp.OrderCooldown;

        // Beside the buyer, not on top of them. Mover coordinates, so a buyer inside a locker or a mech still gets the
        //      pod beside whatever holds them, rather than inside it.
        var dropCoordinates = _transformSystem.GetMoverCoordinates(buyerUid).Offset(_random.NextAngle().ToVec() * tabletEntity.Comp.PodDistance);
        _supplyPodSystem.SpawnPodWithContents(tabletEntity.Comp.PodPrototype, dropCoordinates, contents);

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-order-sent"), tabletEntity.Owner, buyerUid, type: PopupType.Medium);
        UpdateUis(accountEntity);
        return true;
    }

    /// <summary>
    ///     The rank, counted from zero, of an account.
    /// </summary>
    public int GetRank(Entity<KsMinerAccountComponent> accountEntity)
    {
        return ProtoMan.TryIndex(accountEntity.Comp.RankLadder, out var ladderPrototype)
            ? ladderPrototype.GetRank(accountEntity.Comp.Reputation)
            : 0;
    }

    public bool TryGetAccount(Entity<KsSalvageTabletComponent> tabletEntity, out Entity<KsMinerAccountComponent> accountEntity)
    {
        accountEntity = default;
        if (tabletEntity.Comp.BoundCard is not { } cardUid || !_accountQuery.TryComp(cardUid, out var accountComponent))
            return false;

        accountEntity = (cardUid, accountComponent);
        return true;
    }

    /// <summary>
    ///     Whoever carries the account's ID card right now, however deep: in a hand, a pocket, a PDA or a wallet. Worked
    ///         out each time rather than stored, so it is never stale and follows the card when it changes hands.
    /// </summary>
    /// <returns>False when nobody carries it: on the floor, in a locker, or anywhere else a card legitimately is.</returns>
    public bool TryGetCardHolder(Entity<KsMinerAccountComponent> accountEntity, out EntityUid holderUid)
    {
        holderUid = default;

        // Accounts are only ever made on ID cards, at login. One anywhere else is a bug.
        if (!_idCardQuery.HasComp(accountEntity.Owner))
        {
            Log.Error($"{ToPrettyString(accountEntity.Owner)} has a {nameof(KsMinerAccountComponent)} but is not an ID card.");
            return false;
        }

        var currentUid = accountEntity.Owner;
        while (_containerSystem.TryGetContainingContainer(currentUid, out var container))
        {
            currentUid = container.Owner;
            if (!_mobStateQuery.HasComp(currentUid))
                continue;

            holderUid = currentUid;
            return true;
        }

        return false;
    }

    private void Complete(Entity<KsMinerAccountComponent> accountEntity, KsSalvageActiveTask task, KsSalvageTaskPrototype taskPrototype)
    {
        var rankBefore = GetRank(accountEntity);
        var points = taskPrototype.PointsPerUnit * task.Amount;
        accountEntity.Comp.Points += points;
        accountEntity.Comp.Reputation += taskPrototype.Reputation;

        // Paid either way; only the news needs someone carrying the card to hear it.
        if (!TryGetCardHolder(accountEntity, out var salvagerUid))
            return;

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-task-complete", ("points", points), ("reputation", taskPrototype.Reputation)),
            salvagerUid,
            salvagerUid,
            type: PopupType.Medium);
        PlayToSalvager(accountEntity, salvagerUid, tabletComponent => tabletComponent.CompleteSound);

        if (GetRank(accountEntity) > rankBefore)
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-rank-up"), salvagerUid, salvagerUid, type: PopupType.Large);
    }

    /// <summary>
    ///     Tracked tasks ran out of time, and are gone from the account already: every tablet logged in with it sounds
    ///         where it is. A salvager away from their tablet hears nothing.
    /// </summary>
    private void Fail(Entity<KsMinerAccountComponent> accountEntity)
    {
        foreach (var tabletUid in accountEntity.Comp.Tablets)
        {
            if (_tabletQuery.TryComp(tabletUid, out var tabletComponent))
                _audioSystem.PlayPvs(tabletComponent.FailSound, tabletUid);
        }
    }

    /// <summary>
    ///     Plays one of the sounds of a tablet logged in with the account, to its salvager alone.
    /// </summary>
    private void PlayToSalvager(Entity<KsMinerAccountComponent> accountEntity, EntityUid salvagerUid, Func<KsSalvageTabletComponent, SoundSpecifier?> pickSound)
    {
        foreach (var tabletUid in accountEntity.Comp.Tablets)
        {
            if (!_tabletQuery.TryComp(tabletUid, out var tabletComponent))
                continue;

            _audioSystem.PlayGlobal(pickSound(tabletComponent), salvagerUid);
            return;
        }
    }

    private void FillTasks(Entity<KsMinerAccountComponent> accountEntity)
    {
        while (accountEntity.Comp.Tasks.Count < accountEntity.Comp.TaskSlots)
        {
            var task = PickTask(accountEntity);
            if (task == null)
                return;

            accountEntity.Comp.Tasks.Add(task);
        }
    }

    /// <summary>
    ///     Picks a task the account's rank is offered and that it isn't already doing, by weight.
    /// </summary>
    private KsSalvageActiveTask? PickTask(Entity<KsMinerAccountComponent> accountEntity)
    {
        var rank = GetRank(accountEntity);
        var totalWeight = 0f;
        foreach (var taskPrototype in ProtoMan.EnumeratePrototypes<KsSalvageTaskPrototype>())
        {
            if (IsOffered(accountEntity, taskPrototype, rank))
                totalWeight += taskPrototype.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        var roll = _random.NextFloat(totalWeight);
        KsSalvageTaskPrototype? picked = null;
        foreach (var taskPrototype in ProtoMan.EnumeratePrototypes<KsSalvageTaskPrototype>())
        {
            if (!IsOffered(accountEntity, taskPrototype, rank))
                continue;

            picked = taskPrototype;
            roll -= taskPrototype.Weight;
            if (roll < 0f)
                break;
        }

        if (picked == null)
            return null;

        return new KsSalvageActiveTask
        {
            Task = picked.ID,
            Amount = _random.Next(picked.MinAmount, Math.Max(picked.MinAmount, picked.MaxAmount) + 1),
        };
    }

    private static bool IsOffered(Entity<KsMinerAccountComponent> accountEntity, KsSalvageTaskPrototype taskPrototype, int rank)
    {
        if (taskPrototype.MinRank > rank || taskPrototype.Weight <= 0f)
            return false;

        foreach (var activeTask in accountEntity.Comp.Tasks)
        {
            if (activeTask.Task == taskPrototype.ID)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Refreshes every open tablet logged in with the card. The state carries end times rather than time left, so
    ///         nothing needs sending just because time passed: only when something in it changes.
    /// </summary>
    private void UpdateUis(Entity<KsMinerAccountComponent> accountEntity)
    {
        foreach (var tabletUid in accountEntity.Comp.Tablets)
        {
            if (_tabletQuery.TryComp(tabletUid, out var tabletComponent))
                UpdateUi((tabletUid, tabletComponent));
        }
    }

    /// <summary>
    ///     Every open tablet shows the silo, so a change to it refreshes them all.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnSiloReportChanged(ref KsSiloReportChangedEvent args)
    {
        var tabletEnumerator = EntityQueryEnumerator<KsSalvageTabletComponent>();
        while (tabletEnumerator.MoveNext(out var tabletUid, out var tabletComponent))
        {
            UpdateUi((tabletUid, tabletComponent));
        }
    }

    /// <summary>
    ///     The portrait is whoever carries the card, so a card moving in or out of something refreshes it. A card moved
    ///         inside something else (a PDA handed over) isn't told, and shows its new holder with the next change.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnCardInserted(Entity<KsMinerAccountComponent> entity, ref EntGotInsertedIntoContainerMessage args)
    {
        UpdateUis(entity);
    }

    [SubscribeLocalEvent]
    private void OnCardRemoved(Entity<KsMinerAccountComponent> entity, ref EntGotRemovedFromContainerMessage args)
    {
        UpdateUis(entity);
    }

    /// <summary>
    ///     A tablet saved logged in comes back logged in, so it rejoins its card's set.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnTabletStartup(Entity<KsSalvageTabletComponent> entity, ref ComponentStartup args)
    {
        if (entity.Comp.BoundCard is { } cardUid && _accountQuery.TryComp(cardUid, out var accountComponent))
            accountComponent.Tablets.Add(entity.Owner);
    }

    [SubscribeLocalEvent]
    private void OnTabletShutdown(Entity<KsSalvageTabletComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.BoundCard is { } cardUid && _accountQuery.TryComp(cardUid, out var accountComponent))
            accountComponent.Tablets.Remove(entity.Owner);
    }

    /// <summary>
    ///     The card's times are absolute, so a pause would otherwise eat into them: every one moves on by however long
    ///         the card was paused, task end times included.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnAccountUnpaused(Entity<KsMinerAccountComponent> entity, ref EntityUnpausedEvent args)
    {
        entity.Comp.NextDropTime += args.PausedTime;
        entity.Comp.NextOrderTime += args.PausedTime;
        foreach (var task in entity.Comp.Tasks)
        {
            if (task.EndTime is { } endTime)
                task.EndTime = endTime + args.PausedTime;
        }

        UpdateUis(entity);
    }

    /// <summary>
    ///     Anything but logging in or out takes the access the tablet asks for, checked on every message rather than
    ///         only at login: a tablet left logged in must not let whoever picks it up spend for its salvager.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnMessageAttempt(Entity<KsSalvageTabletComponent> entity, ref BoundUserInterfaceMessageAttempt args)
    {
        if (args.Message is KsSalvageTabletLoginMessage or KsSalvageTabletLogoutMessage)
            return;

        if (!TryFindIdCard(args.Actor, out var idCardEntity) ||
            !_accessQuery.TryComp(idCardEntity.Owner, out var accessComponent) ||
            !accessComponent.Tags.Contains(entity.Comp.RequiredAccess))
            args.Cancel();
    }

    private void UpdateUi(Entity<KsSalvageTabletComponent> tabletEntity)
    {
        if (!_userInterfaceSystem.IsUiOpen(tabletEntity.Owner, KsSalvageTabletUiKey.Key))
            return;

        var state = new KsSalvageTabletBuiState();
        if (TryGetAccount(tabletEntity, out var accountEntity))
        {
            state.LoggedIn = true;
            state.Points = accountEntity.Comp.Points;
            state.Reputation = accountEntity.Comp.Reputation;
            state.Tasks = [.. accountEntity.Comp.Tasks];
            state.NextDropTime = accountEntity.Comp.NextDropTime;
            state.NextOrderTime = accountEntity.Comp.NextOrderTime;
            _materialRequestSystem.GetSiloReport(state.Materials, alwaysShown: tabletEntity.Comp.ShownMaterials);

            if (TryGetCardHolder(accountEntity, out var holderUid))
                state.Owner = GetNetEntity(holderUid);

            if (_idCardQuery.TryComp(accountEntity.Owner, out var idCardComponent))
            {
                state.OwnerName = idCardComponent.FullName ?? string.Empty;
                state.JobTitle = idCardComponent.LocalizedJobTitle ?? string.Empty;
            }
        }

        _userInterfaceSystem.SetUiState(tabletEntity.Owner, KsSalvageTabletUiKey.Key, state);
    }

    // The handlers below take a uid and component rather than an Entity<T>: BUI messages and BoundUIOpenedEvent are
    //      by-value class events, and the engine has an Entity<T> handler only for by-ref ones.

    [SubscribeLocalEvent]
    private void OnUiOpened(EntityUid uid, KsSalvageTabletComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnLoginMessage(EntityUid uid, KsSalvageTabletComponent component, KsSalvageTabletLoginMessage args)
    {
        TryLogin((uid, component), args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnLogoutMessage(EntityUid uid, KsSalvageTabletComponent component, KsSalvageTabletLogoutMessage args)
    {
        Logout((uid, component));
    }

    [SubscribeLocalEvent]
    private void OnTrackTaskMessage(EntityUid uid, KsSalvageTabletComponent component, KsSalvageTabletTrackTaskMessage args)
    {
        TryTrackTask((uid, component), args.Task);
    }

    [SubscribeLocalEvent]
    private void OnDropTaskMessage(EntityUid uid, KsSalvageTabletComponent component, KsSalvageTabletDropTaskMessage args)
    {
        TryDropTask((uid, component), args.Task, userUid: args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnOrderMessage(EntityUid uid, KsSalvageTabletComponent component, KsSalvageTabletOrderMessage args)
    {
        var accepted = TryOrder((uid, component), args.Actor, args.Cart);
        _userInterfaceSystem.ServerSendUiMessage(uid, KsSalvageTabletUiKey.Key, new KsSalvageTabletOrderResultMessage(accepted), args.Actor);
    }

    /// <summary>
    ///     Scanning a body for a task.
    /// </summary>
    [SubscribeLocalEvent]
    private void OnAfterInteract(Entity<KsSalvageTabletComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } targetUid || !_mobStateSystem.IsDead(targetUid))
            return;

        args.Handled = true;

        if (!TryGetAccount(entity, out _))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-scan-logged-out"), entity.Owner, args.User);
            return;
        }

        _doAfterSystem.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, entity.Comp.ScanDelay, new KsSalvageTabletScanDoAfterEvent(), entity.Owner, target: targetUid, used: entity.Owner)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
    }

    [SubscribeLocalEvent]
    private void OnScanDoAfter(Entity<KsSalvageTabletComponent> entity, ref KsSalvageTabletScanDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } targetUid || !_mobStateSystem.IsDead(targetUid))
            return;

        args.Handled = true;

        if (_scannedQuery.HasComp(targetUid))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-scan-already"), targetUid, args.User);
            return;
        }

        if (!TryGetAccount(entity, out var accountEntity))
            return;

        var targetPrototype = Prototype(targetUid)?.ID;
        if (targetPrototype == null ||
            !Advance<KsSalvageCorpseObjective>(accountEntity.AsNullable(), objective => objective.Prototypes.Contains(targetPrototype)))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-scan-unwanted"), targetUid, args.User);
            return;
        }

        EnsureComp<KsSalvageTabletScannedComponent>(targetUid);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-scan-counted"), targetUid, args.User);
    }
}
