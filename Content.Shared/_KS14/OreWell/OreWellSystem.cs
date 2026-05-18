using System.Linq;
using Content.Shared.Examine;
using Content.Shared.Stacks;
using Robust.Shared.Collections;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared._KS14.OreWell;

/// <summary>
///     1984
/// </summary>
public sealed class OreWellSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly INetManager _netManager = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveOreWellComponent, ExaminedEvent>(OnExamined);
    }

    private static float Quantize(float value)
        => MathF.Floor(value * 100f + 0.5f) / 100f;

    private void OnExamined(Entity<ActiveOreWellComponent> entity, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using var _ = args.PushGroup(nameof(ActiveOreWellComponent));
        if (entity.Comp.ResourceTypes.Length == 0)
        {
            args.PushMarkup(Loc.GetString("ks-specific-orewell-examined-nothing"));
            return;
        }

        // Although rate is in ore/sec, it is displayed in ore/min
        args.PushMarkup(Loc.GetString("ks-specific-orewell-examined", ("rate", (entity.Comp.IndividualResourceRate * 60).ToString("F1"))));
        foreach (var typeId in entity.Comp.ResourceTypes)
        {
            var type = _prototypeManager.Index(typeId);
            args.PushMarkup(Loc.GetString(type.Name));
        }
    }

    private void InitSettings(Entity<ActiveOreWellComponent> entity)
    {
        // Let it throw
        var setting = _prototypeManager.Index(entity.Comp.SettingId);
        var typeCount = _robustRandom.Next(setting.ResourceCountRange.X, setting.ResourceCountRange.Y);

        var possibleTypes = setting.PossibleResourceTypes.ToList();
        var pickedTypes = new ValueList<ProtoId<StackPrototype>>();

        for (var i = 0; i < typeCount; i++)
            pickedTypes.Add(possibleTypes.RemoveSwap(_robustRandom.Next(possibleTypes.Count)));

        entity.Comp.ResourceTypes = [.. pickedTypes];
        entity.Comp.IndividualResourceRate = Quantize(_robustRandom.NextFloat(setting.TotalResourceRateRange.X, setting.TotalResourceRateRange.Y) / pickedTypes.Count);

        Dirty(entity);
    }

    public void GenerateOreWellWithSettings(Entity<ActiveOreWellComponent?> entity, ProtoId<OreWellSettingPrototype> settingId)
    {
        if (_netManager.IsClient)
            return;

        if (Resolve(entity.Owner, ref entity.Comp, logMissing: false))
            return;

        var component = EntityManager.ComponentFactory.GetComponent<ActiveOreWellComponent>();
        component.SettingId = settingId;

        AddComp(entity, component);
        InitSettings((entity, component));
    }

    /// <summary>
    ///     Gets all of the material generated in one second, multiplied
    ///         by something. Which can, coincidentally, be time.
    /// </summary>
    public Dictionary<ProtoId<StackPrototype>, float> GenerateResourcesAndTake(float multiplier)
    {
        var amounts = new Dictionary<ProtoId<StackPrototype>, float>();

        var eqe = EntityQueryEnumerator<ActiveOreWellComponent>();
        while (eqe.MoveNext(out var wellComponent))
        {
            var individualRate = wellComponent.IndividualResourceRate;
            foreach (var resourceId in wellComponent.ResourceTypes)
            {
                var amount = amounts.GetValueOrDefault(resourceId);
                amount += individualRate * multiplier;

                amounts[resourceId] = amount;
            }
        }

        return amounts;
    }
}
