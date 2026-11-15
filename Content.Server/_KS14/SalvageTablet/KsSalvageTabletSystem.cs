using System.Diagnostics.CodeAnalysis;
using Content.Server._KS14.SupplyPod;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared._KS14.SalvageTablet;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.SalvageTablet;

/// <summary>
///     Runs the S.P.A.M. tablets and the accounts behind them: logging in, dealing, tracking and dropping tasks, counting
///         progress, and paying out mining points and reputation.
///
///     Whatever sees a task's objective happen reports it here with <see cref="Advance"/>.
/// </summary>
public sealed partial class KsSalvageTabletSystem : EntitySystem
{
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private SharedHandsSystem _handsSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedDoAfterSystem _doAfterSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private MobStateSystem _mobStateSystem = default!;
    [Dependency] private SupplyPodSystem _supplyPodSystem = default!;
    [Dependency] private KsMaterialRequestSystem _materialRequestSystem = default!;
    [Dependency] private EntityQuery<KsMinerAccountComponent> _accountQuery = default!;

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
                UpdateUis(cardUid);
        }

        // The silo and the bank change under an open tablet, and nothing says when.
        var tabletEnumerator = EntityQueryEnumerator<KsSalvageTabletComponent>();
        while (tabletEnumerator.MoveNext(out var tabletUid, out var tabletComponent))
        {
            UpdateUi((tabletUid, tabletComponent));
        }
    }

    /// <summary>
    ///     Logs the tablet in with the ID the user carries. The ID gets an account if it has none.
    /// </summary>
    public bool TryLogin(Entity<KsSalvageTabletComponent> tablet, EntityUid user)
    {
        if (!TryFindIdCard(user, out var idCard))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-login-no-id"), tablet.Owner, user);
            return false;
        }

        if (!TryComp<AccessComponent>(idCard.Owner, out var accessComponent) ||
            !accessComponent.Tags.Contains(tablet.Comp.RequiredAccess))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-login-denied"), tablet.Owner, user);
            return false;
        }

        var accountComponent = EnsureComp<KsMinerAccountComponent>(idCard.Owner);
        accountComponent.Salvager = user;
        accountComponent.RankLadder = tablet.Comp.RankLadder;
        accountComponent.TaskSlots = tablet.Comp.TaskSlots;
        FillTasks((idCard.Owner, accountComponent));

        tablet.Comp.BoundCard = idCard.Owner;
        Dirty(tablet);
        UpdateUi(tablet);
        return true;
    }

    /// <summary>
    ///     The ID the user wears or holds. The tablet is in their active hand, so an ID in the other one counts too.
    /// </summary>
    private bool TryFindIdCard(EntityUid user, out Entity<IdCardComponent> idCard)
    {
        if (_idCardSystem.TryFindIdCard(user, out idCard))
            return true;

        foreach (var heldUid in _handsSystem.EnumerateHeld(user))
        {
            if (_idCardSystem.TryGetIdCard(heldUid, out idCard))
                return true;
        }

        return false;
    }

    public void Logout(Entity<KsSalvageTabletComponent> tablet)
    {
        tablet.Comp.BoundCard = null;
        Dirty(tablet);
        UpdateUi(tablet);
    }

    /// <summary>
    ///     Takes a task on: from now it counts progress, and its time runs. An account tracks only so many at once,
    ///         and a tracked task stays tracked until it is finished, failed or dropped.
    /// </summary>
    public bool TryTrackTask(Entity<KsSalvageTabletComponent> tablet, ProtoId<KsSalvageTaskPrototype> taskId)
    {
        if (!TryGetAccount(tablet, out var account) ||
            !TryGetTask(account, taskId, out var task) ||
            task.Tracked ||
            !_prototypeManager.TryIndex(taskId, out var taskPrototype))
            return false;

        var tracked = 0;
        foreach (var activeTask in account.Comp.Tasks)
        {
            if (activeTask.Tracked)
                tracked++;
        }

        if (tracked >= tablet.Comp.MaxTrackedTasks)
            return false;

        task.Tracked = true;
        task.EndTime = _gameTiming.CurTime + taskPrototype.Duration;
        UpdateUis(account.Owner);
        return true;
    }

    /// <summary>
    ///     Throws a task away, tracked or not, if the account's drop cooldown is over. Another task takes its place if
    ///         the account's rank is offered one it isn't already doing; otherwise the slot stays empty until the cooldown
    ///         is over.
    /// </summary>
    /// <param name="user">Who is told why a drop was refused, if anyone.</param>
    public bool TryDropTask(Entity<KsSalvageTabletComponent> tablet, ProtoId<KsSalvageTaskPrototype> taskId, EntityUid? user = null)
    {
        if (!TryGetAccount(tablet, out var account) || !TryGetTask(account, taskId, out var task))
            return false;

        if (_gameTiming.CurTime < account.Comp.NextDropTime)
        {
            if (user != null)
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-task-drop-refused"), tablet.Owner, user.Value);
            return false;
        }

        // The old task stays in the list while the new one is picked, so the drop never deals the same task back.
        var index = account.Comp.Tasks.IndexOf(task);
        if (PickTask(account) is { } replacement)
            account.Comp.Tasks[index] = replacement;
        else
            account.Comp.Tasks.RemoveAt(index);

        account.Comp.NextDropTime = _gameTiming.CurTime + tablet.Comp.DropCooldown;
        UpdateUis(account.Owner);
        return true;
    }

    private static bool TryGetTask(Entity<KsMinerAccountComponent> account, ProtoId<KsSalvageTaskPrototype> taskId, [NotNullWhen(true)] out KsSalvageActiveTask? task)
    {
        foreach (var activeTask in account.Comp.Tasks)
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
    public bool Advance<T>(Entity<KsMinerAccountComponent?> account, Predicate<T> matches, int amount = 1) where T : KsSalvageTaskObjective
    {
        if (amount <= 0 || !_accountQuery.Resolve(account.Owner, ref account.Comp, logMissing: false))
            return false;

        var advanced = false;
        var tasks = account.Comp.Tasks;
        for (var index = tasks.Count - 1; index >= 0; index--)
        {
            var task = tasks[index];
            if (!task.Tracked ||
                !_prototypeManager.TryIndex(task.Task, out var taskPrototype) ||
                taskPrototype.Objective is not T objective ||
                !matches(objective))
                continue;

            advanced = true;
            task.Progress = Math.Min(task.Progress + amount, task.Amount);
            if (task.Progress < task.Amount)
                continue;

            tasks.RemoveAt(index);
            Complete((account.Owner, account.Comp), task, taskPrototype);
        }

        if (!advanced)
            return false;

        FillTasks((account.Owner, account.Comp));
        UpdateUis(account.Owner);
        return true;
    }

    /// <summary>
    ///     Pays mining points into an account.
    /// </summary>
    public void AddPoints(Entity<KsMinerAccountComponent> account, int points)
    {
        account.Comp.Points += points;
        UpdateUis(account.Owner);
    }

    /// <summary>
    ///     Buys the cart with the account's mining points and drops it next to the buyer in a pod. All of it or
    ///         nothing: one product above the account's rank, or a total above its points, refuses the order.
    /// </summary>
    public bool TryOrder(Entity<KsSalvageTabletComponent> tablet, EntityUid buyer, Dictionary<ProtoId<KsSalvageProductPrototype>, int> cart)
    {
        if (!TryGetAccount(tablet, out var account) || cart.Count == 0)
            return false;

        if (_gameTiming.CurTime < account.Comp.NextOrderTime)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-order-wait"), tablet.Owner, buyer);
            return false;
        }

        var rank = GetRank(account);
        var total = 0;
        var contents = new List<EntProtoId>();
        foreach (var (productId, amount) in cart)
        {
            if (amount <= 0 ||
                amount > tablet.Comp.MaxOrderAmount ||
                !_prototypeManager.TryIndex(productId, out var productPrototype) ||
                productPrototype.MinRank > rank)
            {
                _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-order-refused"), tablet.Owner, buyer);
                return false;
            }

            total += productPrototype.Cost * amount;
            for (var unit = 0; unit < amount; unit++)
            {
                contents.Add(productPrototype.Entity);
            }
        }

        if (total > account.Comp.Points)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-order-too-expensive"), tablet.Owner, buyer);
            return false;
        }

        account.Comp.Points -= total;
        account.Comp.NextOrderTime = _gameTiming.CurTime + tablet.Comp.OrderCooldown;

        // Beside the buyer, not on top of them.
        var dropCoordinates = Transform(buyer).Coordinates.Offset(_random.NextAngle().ToVec() * tablet.Comp.PodDistance);
        _supplyPodSystem.SpawnPodWithContents(tablet.Comp.PodPrototype, dropCoordinates, contents);

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-order-sent"), tablet.Owner, buyer, PopupType.Medium);
        UpdateUis(account.Owner);
        return true;
    }

    /// <summary>
    ///     The rank, counted from zero, of an account.
    /// </summary>
    public int GetRank(Entity<KsMinerAccountComponent> account)
    {
        return _prototypeManager.TryIndex(account.Comp.RankLadder, out var ladderPrototype)
            ? ladderPrototype.GetRank(account.Comp.Reputation)
            : 0;
    }

    public bool TryGetAccount(Entity<KsSalvageTabletComponent> tablet, out Entity<KsMinerAccountComponent> account)
    {
        account = default;
        if (tablet.Comp.BoundCard is not { } cardUid || !_accountQuery.TryComp(cardUid, out var accountComponent))
            return false;

        account = (cardUid, accountComponent);
        return true;
    }

    private void Complete(Entity<KsMinerAccountComponent> account, KsSalvageActiveTask task, KsSalvageTaskPrototype taskPrototype)
    {
        var rankBefore = GetRank(account);
        var points = taskPrototype.PointsPerUnit * task.Amount;
        account.Comp.Points += points;
        account.Comp.Reputation += taskPrototype.Reputation;

        if (account.Comp.Salvager is not { } ownerUid || TerminatingOrDeleted(ownerUid))
            return;

        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-task-complete", ("points", points), ("reputation", taskPrototype.Reputation)),
            ownerUid,
            ownerUid,
            PopupType.Medium);
        PlayToSalvager(account, ownerUid, tabletComponent => tabletComponent.CompleteSound);

        if (GetRank(account) > rankBefore)
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-rank-up"), ownerUid, ownerUid, PopupType.Large);
    }

    /// <summary>
    ///     Tracked tasks ran out of time, and are gone from the account already: every tablet logged in with it sounds
    ///         where it is. A salvager away from their tablet hears nothing.
    /// </summary>
    private void Fail(Entity<KsMinerAccountComponent> account)
    {
        var tabletEnumerator = EntityQueryEnumerator<KsSalvageTabletComponent>();
        while (tabletEnumerator.MoveNext(out var tabletUid, out var tabletComponent))
        {
            if (tabletComponent.BoundCard == account.Owner)
                _audioSystem.PlayPvs(tabletComponent.FailSound, tabletUid);
        }
    }

    /// <summary>
    ///     Plays one of the sounds of a tablet logged in with the account, to its salvager alone.
    /// </summary>
    private void PlayToSalvager(Entity<KsMinerAccountComponent> account, EntityUid salvagerUid, Func<KsSalvageTabletComponent, SoundSpecifier?> pickSound)
    {
        var tabletEnumerator = EntityQueryEnumerator<KsSalvageTabletComponent>();
        while (tabletEnumerator.MoveNext(out _, out var tabletComponent))
        {
            if (tabletComponent.BoundCard != account.Owner)
                continue;

            _audioSystem.PlayGlobal(pickSound(tabletComponent), salvagerUid);
            return;
        }
    }

    private void FillTasks(Entity<KsMinerAccountComponent> account)
    {
        while (account.Comp.Tasks.Count < account.Comp.TaskSlots)
        {
            var task = PickTask(account);
            if (task == null)
                return;

            account.Comp.Tasks.Add(task);
        }
    }

    /// <summary>
    ///     Picks a task the account's rank is offered and that it isn't already doing, by weight.
    /// </summary>
    private KsSalvageActiveTask? PickTask(Entity<KsMinerAccountComponent> account)
    {
        var rank = GetRank(account);
        var totalWeight = 0f;
        foreach (var taskPrototype in _prototypeManager.EnumeratePrototypes<KsSalvageTaskPrototype>())
        {
            if (IsOffered(account, taskPrototype, rank))
                totalWeight += taskPrototype.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        var roll = _random.NextFloat(totalWeight);
        KsSalvageTaskPrototype? picked = null;
        foreach (var taskPrototype in _prototypeManager.EnumeratePrototypes<KsSalvageTaskPrototype>())
        {
            if (!IsOffered(account, taskPrototype, rank))
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

    private static bool IsOffered(Entity<KsMinerAccountComponent> account, KsSalvageTaskPrototype taskPrototype, int rank)
    {
        if (taskPrototype.MinRank > rank || taskPrototype.Weight <= 0f)
            return false;

        foreach (var activeTask in account.Comp.Tasks)
        {
            if (activeTask.Task == taskPrototype.ID)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Refreshes every open tablet logged in with the card.
    /// </summary>
    private void UpdateUis(EntityUid cardUid)
    {
        var tabletEnumerator = EntityQueryEnumerator<KsSalvageTabletComponent>();
        while (tabletEnumerator.MoveNext(out var tabletUid, out var tabletComponent))
        {
            if (tabletComponent.BoundCard == cardUid)
                UpdateUi((tabletUid, tabletComponent));
        }
    }

    private void UpdateUi(Entity<KsSalvageTabletComponent> tablet)
    {
        if (!_uiSystem.IsUiOpen(tablet.Owner, KsSalvageTabletUiKey.Key))
            return;

        var state = new KsSalvageTabletBuiState();
        if (TryGetAccount(tablet, out var account))
        {
            state.LoggedIn = true;
            state.Points = account.Comp.Points;
            state.Reputation = account.Comp.Reputation;
            state.Tasks = [.. account.Comp.Tasks];
            state.NextDropTime = account.Comp.NextDropTime;
            state.NextOrderTime = account.Comp.NextOrderTime;
            _materialRequestSystem.GetSiloReport(state.Materials, alwaysShown: tablet.Comp.ShownMaterials);

            if (account.Comp.Salvager is { } ownerUid && !TerminatingOrDeleted(ownerUid))
                state.Owner = GetNetEntity(ownerUid);

            if (TryComp<IdCardComponent>(account.Owner, out var idCardComponent))
            {
                state.OwnerName = idCardComponent.FullName ?? string.Empty;
                state.JobTitle = idCardComponent.LocalizedJobTitle ?? string.Empty;
            }
        }

        _uiSystem.SetUiState(tablet.Owner, KsSalvageTabletUiKey.Key, state);
    }

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
        TryDropTask((uid, component), args.Task, user: args.Actor);
    }

    [SubscribeLocalEvent]
    private void OnOrderMessage(EntityUid uid, KsSalvageTabletComponent component, KsSalvageTabletOrderMessage args)
    {
        TryOrder((uid, component), args.Actor, args.Cart);
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

        if (HasComp<KsSalvageTabletScannedComponent>(targetUid))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-scan-already"), targetUid, args.User);
            return;
        }

        if (!TryGetAccount(entity, out var account))
            return;

        var targetPrototype = Prototype(targetUid)?.ID;
        if (targetPrototype == null ||
            !Advance<KsSalvageCorpseObjective>(account.AsNullable(), objective => objective.Prototypes.Contains(targetPrototype)))
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-scan-unwanted"), targetUid, args.User);
            return;
        }

        EnsureComp<KsSalvageTabletScannedComponent>(targetUid);
        _popupSystem.PopupEntity(KsPopupMessage.Create("ks-salvage-tablet-scan-counted"), targetUid, args.User);
    }
}
