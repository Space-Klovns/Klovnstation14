using System.Linq;
using Content.Shared.Examine;
using Content.Shared.Materials;
using Robust.Shared.Collections;
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

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActiveOreWellComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ActiveOreWellComponent, MapInitEvent>(OnMapInit);
    }

    private void OnExamined(Entity<ActiveOreWellComponent> entity, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using var _ = args.PushGroup(nameof(ActiveOreWellComponent), priority: 2);

        if (entity.Comp.ResourceTypes.Length == 0)
        {
            args.PushMarkup(Loc.GetString("ks-specific-orewell-examined-nothing"));
            return;
        }

        // Although rate is in ore/sec, it is displayed in ore/min
        args.PushGroup(Loc.GetString("ks-specific-orewell-examined", ("rate", entity.Comp.IndividualResourceRate * 60)), priority: 2);
        foreach (var typeId in entity.Comp.ResourceTypes)
        {
            var type = _prototypeManager.Index(typeId);
            args.PushMarkup(Loc.GetString(type.Name));
        }
    }

    private void OnMapInit(Entity<ActiveOreWellComponent> entity, ref MapInitEvent args)
    {
        InitSettings(entity);
    }

    private void InitSettings(Entity<ActiveOreWellComponent> entity)
    {
        // Let it throw
        var setting = _prototypeManager.Index(entity.Comp.SettingId);
        var typeCount = _robustRandom.Next(setting.ResourceCountRange.X, setting.ResourceCountRange.Y);

        var possibleTypes = setting.PossibleResourceTypes.ToList();
        var pickedTypes = new ValueList<ProtoId<MaterialPrototype>>();

        for (var i = 0; i < typeCount; i++)
            pickedTypes.Add(possibleTypes.RemoveSwap(_robustRandom.Next(possibleTypes.Count)));

        entity.Comp.ResourceTypes = [.. pickedTypes];
        entity.Comp.IndividualResourceRate = _robustRandom.NextFloat(setting.TotalResourceRateRange.X, setting.TotalResourceRateRange.Y) / pickedTypes.Count;
    }

    public void GenerateOreWellWithSettings(Entity<ActiveOreWellComponent?> entity, ProtoId<OreWellSettingPrototype> settingId)
    {
        var component = entity.Comp ?? EnsureComp<ActiveOreWellComponent>(entity.Owner);

        component.SettingId = settingId;
        InitSettings((entity, component));
    }

    /// <summary>
    ///     Gets all of the material generated in one second, multiplied
    ///         by something. Which can, coincidentally, be time.
    /// </summary>
    public Dictionary<ProtoId<MaterialPrototype>, float> GenerateResourcesAndTake(float multiplier)
    {
        var amounts = new Dictionary<ProtoId<MaterialPrototype>, float>();

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
