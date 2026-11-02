using System.Numerics;
using Content.Server._KS14.Chemistry.Explosion;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared._KS14.CCVar;
using Content.Shared._KS14.Fluidmos;
using Content.Shared._KS14.PopupLocalization;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Destructible;
using Content.Shared.Jittering;
using Content.Shared.NodeContainer;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._KS14.Fluidmos.CookOff;

/// <summary>
///     Pipe nets holding explosive fluid hotter than it cooks off at
///         (<see cref="ReagentPrototype.KsCookOffTemperature"/>) rattle and groan, bends first as the weakest link, then
///         burst in a chain: the first bend, then the rest in order of distance from it, each spraying burning fluid
///         and exploding.
/// </summary>
/// <remarks>
///     <para>
///         The explosive power per node sets how long it takes, from <see cref="MaxSeconds"/> for a net barely past
///             <see cref="MinPowerPerNode"/> down to <see cref="MinSeconds"/>, and at <see cref="InstantPowerPerNode"/>
///             and above it bursts on the spot. The total power sets how many pipes burst: one per
///             <see cref="PowerPerRupture"/>, up to every node in the net. <see cref="KsCCVars.AtmosPipeCookOffSpillFraction"/> of the explosive
///             fluid sprays out of the ruptures; the rest goes into the explosions.
///     </para>
///     <para>
///         Only the net's fluid counts: liquid, solid and unlinked vapor. Linked gas, such as plasma gas, is ordinary
///             atmos and burns as it always has; a hot plasma line does not blow up.
///     </para>
/// </remarks>
public sealed partial class KsPipeCookOffSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configurationManager = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private KsFluidmosPropertiesSystem _propertiesSystem = default!;
    [Dependency] private KsFluidmosTileSystem _tileSystem = default!;
    [Dependency] private KsSolutionExplosionSystem _solutionExplosionSystem = default!;
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedDestructibleSystem _destructibleSystem = default!;
    [Dependency] private SharedJitteringSystem _jitteringSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;
    [Dependency] private SharedTransformSystem _transformSystem = default!;
    [Dependency] private EntityQuery<KsPipeCookOffComponent> _cookOffQuery = default!;
    [Dependency] private EntityQuery<NodeContainerComponent> _nodeContainerQuery = default!;

    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Explosive power per node, against welding fuel's 1 per unit, below which a net is too dilute to cook off.
    /// </summary>
    public const float MinPowerPerNode = 5f;

    /// <summary>
    ///     Explosive power per node at which a net takes <see cref="ReferenceSeconds"/> to burst. Twice as much takes
    ///         half as long.
    /// </summary>
    public const float ReferencePowerPerNode = 100f;

    public const float ReferenceSeconds = 15f;

    public const float MinSeconds = 3f;

    public const float MaxSeconds = 60f;

    /// <summary>
    ///     Explosive power per node at and above which a net bursts the moment it is hot enough, with no rattling.
    /// </summary>
    public const float InstantPowerPerNode = 1000f;

    /// <summary>
    ///     Explosive power each burst pipe takes: half a full fuel tank.
    /// </summary>
    public const float PowerPerRupture = KsSolutionExplosionSystem.ReferencePower / 2f;

    /// <summary>
    ///     The share of the net's explosive fluid the ruptures spray onto their tiles, as liquid: hot enough, it lights.
    ///         The rest goes into the explosions. <see cref="KsCCVars.AtmosPipeCookOffSpillFraction"/>, clamped to 0–1.
    /// </summary>
    private float _spillFraction;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configurationManager, KsCCVars.AtmosPipeCookOffSpillFraction,
            value => _spillFraction = Math.Clamp(value, 0f, 1f), invokeImmediately: true);
    }

    /// <summary>
    ///     Time between one pipe in a chain bursting and the next.
    /// </summary>
    public static readonly TimeSpan ChainDelay = TimeSpan.FromSeconds(0.25);

    /// <summary>
    ///     Progress at which a net's first pipe groans, and at which it warns it is about to burst.
    /// </summary>
    private const float GroanProgress = 0.5f;
    private const float AboutToBurstProgress = 0.85f;

    private const float MinJitterAmplitude = 2f;
    private const float MaxJitterAmplitude = 20f;
    private const float MinJitterFrequency = 2f;
    private const float MaxJitterFrequency = 8f;

    /// <summary>
    ///     Chance a cooking net groans on a check, from the start of its cook-off to its end.
    /// </summary>
    private const float MinGroanChance = 0.25f;
    private const float MaxGroanChance = 1f;

    private static readonly SoundSpecifier GroanSound = new SoundCollectionSpecifier("MetalScrape",
        AudioParams.Default.WithVariation(0.2f).WithVolume(-4f));

    private static readonly SoundSpecifier BurstSound = new SoundCollectionSpecifier("MetalBreak",
        AudioParams.Default.WithVariation(0.15f));

    private TimeSpan _nextCheck = TimeSpan.Zero;

    /// <summary>
    ///     Nets found cooking off this check: a transient work list, for dropping the targets of every other net.
    /// </summary>
    private readonly HashSet<PipeNet> _cookingNets = new();

    private readonly List<EntityUid> _targets = new();
    private readonly List<EntityUid> _candidates = new();
    private readonly HashSet<EntityUid> _owners = new();
    private readonly List<KsFluidReagentKey> _explosiveKeys = new();
    private readonly List<(EntityUid Uid, KsPipeCookOffComponent Component)> _dueRuptures = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _gameTiming.CurTime;
        if (curTime >= _nextCheck)
        {
            _nextCheck = curTime + CheckInterval;
            CheckNets();
        }

        // Every tick, for the chain's spacing. Few pipes ever carry this component.
        _dueRuptures.Clear();
        var targetQuery = EntityQueryEnumerator<KsPipeCookOffComponent>();
        while (targetQuery.MoveNext(out var uid, out var cookOffComponent))
        {
            if (cookOffComponent.RuptureAt is { } ruptureAt && ruptureAt <= curTime)
                _dueRuptures.Add((uid, cookOffComponent));
        }

        // Outside the walk: bursting deletes the pipe and changes its net.
        foreach (var (uid, cookOffComponent) in _dueRuptures)
            Rupture((uid, cookOffComponent));
    }

    private void CheckNets()
    {
        _cookingNets.Clear();

        var gridQuery = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (gridQuery.MoveNext(out _, out var gridAtmosphereComponent))
        {
            foreach (var net in gridAtmosphereComponent.PipeNets)
            {
                if (net is PipeNet { Air.Fluid.IsEmpty: false } pipeNet)
                    CheckNet(pipeNet);
            }
        }

        // Pipes whose net cooled, ran dry or split off stop rattling. A chain, once started, runs to its end.
        var targetQuery = EntityQueryEnumerator<KsPipeCookOffComponent>();
        while (targetQuery.MoveNext(out var uid, out var cookOffComponent))
        {
            if (cookOffComponent.RuptureAt == null && cookOffComponent.LifeStage <= ComponentLifeStage.Running &&
                !InCookingNet(uid))
                RemCompDeferred(uid, cookOffComponent);
        }
    }

    [SubscribeLocalEvent]
    private void OnShutdown(Entity<KsPipeCookOffComponent> entity, ref ComponentShutdown args)
    {
        if (!Terminating(entity.Owner))
            RemComp<JitteringComponent>(entity.Owner);
    }

    private bool InCookingNet(EntityUid uid)
    {
        if (!_nodeContainerQuery.TryComp(uid, out var nodeContainerComponent))
            return false;

        foreach (var node in nodeContainerComponent.Nodes.Values)
        {
            if (node.NodeGroup is PipeNet net && _cookingNets.Contains(net))
                return true;
        }

        return false;
    }

    private void CheckNet(PipeNet net)
    {
        var fluid = net.Air.Fluid!;
        var nodeCount = net.Nodes.Count;
        if (nodeCount == 0 || GetCookOff(fluid) is not { } cookOff || net.Air.Temperature <= cookOff.CookOffTemperature)
            return;

        var power = cookOff.Power;
        var powerPerNode = power / nodeCount;
        if (powerPerNode < MinPowerPerNode)
            return;

        _cookingNets.Add(net);

        var ruptureCount = Math.Clamp((int)MathF.Ceiling(power / PowerPerRupture), 1, nodeCount);
        var seconds = Math.Clamp(ReferenceSeconds * ReferencePowerPerNode / powerPerNode, MinSeconds, MaxSeconds);

        // The pipes already rattling keep going; the net's progress is theirs, and new ones join at it.
        _targets.Clear();
        _owners.Clear();
        var oldProgress = 0f;
        foreach (var node in net.Nodes)
        {
            if (!_owners.Add(node.Owner) || !_cookOffQuery.TryComp(node.Owner, out var cookOffComponent))
                continue;

            _targets.Add(node.Owner);
            oldProgress = MathF.Max(oldProgress, cookOffComponent.Progress);
        }

        var progress = powerPerNode >= InstantPowerPerNode
            ? 1f
            : oldProgress + (float)CheckInterval.TotalSeconds / seconds;

        if (_targets.Count < ruptureCount)
            PickTargets(net, ruptureCount - _targets.Count);

        if (_targets.Count == 0)
            return;

        foreach (var targetUid in _targets)
        {
            EnsureComp<KsPipeCookOffComponent>(targetUid).Progress = progress;

            var rattle = MathF.Min(progress, 1f);
            _jitteringSystem.AddJitter(targetUid,
                amplitude: MinJitterAmplitude + (MaxJitterAmplitude - MinJitterAmplitude) * rattle,
                frequency: MinJitterFrequency + (MaxJitterFrequency - MinJitterFrequency) * rattle);
        }

        Warn(oldProgress, progress);

        if (progress >= 1f)
            StartChain(net, fluid, power);
    }

    /// <summary>
    ///     Groans now and then, more often as the cook-off goes on, and says so the first time it passes each warning.
    /// </summary>
    private void Warn(float oldProgress, float progress)
    {
        var loudestUid = _targets[0];

        if (oldProgress < AboutToBurstProgress && progress >= AboutToBurstProgress)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-pipe-cookoff-about-to-burst", ("pipe", loudestUid)),
                loudestUid, PopupType.LargeCaution);
        }
        else if (oldProgress < GroanProgress && progress >= GroanProgress)
        {
            _popupSystem.PopupEntity(KsPopupMessage.Create("ks-pipe-cookoff-groan", ("pipe", loudestUid)),
                loudestUid, PopupType.MediumCaution);
        }

        if (_robustRandom.Prob(MinGroanChance + (MaxGroanChance - MinGroanChance) * MathF.Min(progress, 1f)))
            _audioSystem.PlayPvs(GroanSound, _robustRandom.Pick(_targets));
    }

    /// <summary>
    ///     Takes the net's explosive fluid for the blasts and the spray, and times each target's rupture: the first bend
    ///         at once, then the rest in order of distance from it.
    /// </summary>
    private void StartChain(PipeNet net, KsFluidMixture fluid, float power)
    {
        var spill = TakeExplosives(fluid);
        spill.Multiply(1f / _targets.Count);
        var explosionPower = power * (1f - _spillFraction) / _targets.Count;

        var firstIndex = _targets.FindIndex(IsBendPipe);
        if (firstIndex > 0)
            (_targets[0], _targets[firstIndex]) = (_targets[firstIndex], _targets[0]);

        var origin = _transformSystem.GetWorldPosition(_targets[0]);
        _targets.Sort(1, _targets.Count - 1, Comparer<EntityUid>.Create((a, b) =>
            Vector2.DistanceSquared(_transformSystem.GetWorldPosition(a), origin)
                .CompareTo(Vector2.DistanceSquared(_transformSystem.GetWorldPosition(b), origin))));

        var curTime = _gameTiming.CurTime;
        for (var i = 0; i < _targets.Count; i++)
        {
            var cookOffComponent = _cookOffQuery.Comp(_targets[i]);
            cookOffComponent.RuptureAt = curTime + ChainDelay * i;
            cookOffComponent.Power = explosionPower;
            cookOffComponent.Spill = spill.Clone();
            cookOffComponent.SpillTemperature = net.Air.Temperature;
        }
    }

    /// <summary>
    ///     Adds <paramref name="count"/> more of the net's pipes to <see cref="_targets"/>: bends first, the weakest
    ///         link, then the rest at random.
    /// </summary>
    private void PickTargets(PipeNet net, int count)
    {
        _candidates.Clear();
        _owners.Clear();
        _owners.UnionWith(_targets);

        var bendCount = 0;
        foreach (var node in net.Nodes)
        {
            // One entry per entity, however many of its nodes are in the net.
            if (node is not PipeNode pipeNode || TerminatingOrDeleted(node.Owner) || !_owners.Add(node.Owner))
                continue;

            if (IsBend(pipeNode.CurrentPipeDirection))
                _candidates.Insert(bendCount++, node.Owner);
            else
                _candidates.Add(node.Owner);
        }

        // Random among bends, then random among the rest.
        Shuffle(0, bendCount);
        Shuffle(bendCount, _candidates.Count);

        for (var i = 0; i < _candidates.Count && count > 0; i++, count--)
            _targets.Add(_candidates[i]);
    }

    private void Shuffle(int start, int end)
    {
        for (var i = end - 1; i > start; i--)
        {
            var j = _robustRandom.Next(start, i + 1);
            (_candidates[i], _candidates[j]) = (_candidates[j], _candidates[i]);
        }
    }

    private bool IsBendPipe(EntityUid uid)
    {
        if (!_nodeContainerQuery.TryComp(uid, out var nodeContainerComponent))
            return false;

        foreach (var node in nodeContainerComponent.Nodes.Values)
        {
            if (node is PipeNode pipeNode && IsBend(pipeNode.CurrentPipeDirection))
                return true;
        }

        return false;
    }

    private static bool IsBend(PipeDirection direction)
    {
        return direction is PipeDirection.NEBend or PipeDirection.NWBend or PipeDirection.SEBend or PipeDirection.SWBend;
    }

    private void Rupture(Entity<KsPipeCookOffComponent> entity)
    {
        if (TerminatingOrDeleted(entity.Owner))
            return;

        var coordinates = _transformSystem.GetMoverCoordinates(entity.Owner);
        var mapCoordinates = _transformSystem.GetMapCoordinates(entity.Owner);

        // Sprayed first, so the blast can light it.
        if (entity.Comp.Spill is { } spill)
            _tileSystem.QueueSpill(coordinates, spill, entity.Comp.SpillTemperature);

        _audioSystem.PlayPvs(BurstSound, coordinates);
        _destructibleSystem.DestroyEntity(entity.Owner);

        if (mapCoordinates.MapId != MapId.Nullspace)
            _solutionExplosionSystem.QueueExplosion(mapCoordinates, entity.Comp.Power);
    }

    /// <summary>
    ///     The explosive power of a mixture's fluid and the lowest temperature any explosive in it cooks off at, or null
    ///         if nothing in it does.
    /// </summary>
    public (float Power, float CookOffTemperature)? GetCookOff(KsFluidMixture fluid)
    {
        var power = 0f;
        float? cookOffTemperature = null;
        foreach (var (key, amount) in fluid.Reagents)
        {
            if (!ProtoMan.TryIndex(key.Prototype, out var reagentPrototype) ||
                reagentPrototype.KsExplosivePower <= 0f ||
                reagentPrototype.KsCookOffTemperature is not { } temperature)
                continue;

            power += Units(key, amount) * reagentPrototype.KsExplosivePower;
            cookOffTemperature = cookOffTemperature is { } lowest ? MathF.Min(lowest, temperature) : temperature;
        }

        return cookOffTemperature is { } result && power > 0f ? (power, result) : null;
    }

    /// <summary>
    ///     Every phase of one reagent, in units.
    /// </summary>
    private float Units(KsFluidReagentKey key, KsFluidAmount amount)
    {
        var units = amount.Liquid + amount.Solid;
        if (amount.Vapor > 0f && _propertiesSystem.TryGetProperties(key.Prototype, out var properties) && properties.MolesPerUnit > 0f)
            units += amount.Vapor / properties.MolesPerUnit;

        return units;
    }

    /// <summary>
    ///     Removes every explosive reagent from the fluid, so what is left of the net doesn't go off again.
    /// </summary>
    /// <returns><see cref="_spillFraction"/> of them, as liquid, for the ruptures to spray.</returns>
    private KsFluidMixture TakeExplosives(KsFluidMixture fluid)
    {
        var spill = new KsFluidMixture();
        _explosiveKeys.Clear();
        foreach (var (key, amount) in fluid.Reagents)
        {
            if (!ProtoMan.TryIndex(key.Prototype, out var reagentPrototype) || reagentPrototype.KsExplosivePower <= 0f)
                continue;

            _explosiveKeys.Add(key);
            spill.AddLiquid(key, Units(key, amount) * _spillFraction);
        }

        foreach (var key in _explosiveKeys)
            fluid.SetAmount(key, default);

        return spill;
    }
}
