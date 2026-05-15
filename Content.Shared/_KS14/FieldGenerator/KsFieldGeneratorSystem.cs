using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._KS14.FieldGenerator;

// TODO admin logs

public sealed partial class KsFieldGeneratorSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearanceSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!;

    private EntityQuery<KsGeneratedFieldComponent> _fieldQuery;

    public override void Initialize()
    {
        base.Initialize();

        _fieldQuery = GetEntityQuery<KsGeneratedFieldComponent>();

        SubscribeLocalEvent<KsFieldGeneratorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<KsFieldGeneratorComponent, ActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<KsFieldGeneratorComponent, PowerChangedEvent>(OnPowerChanged);

        InitialiseLinking();
    }

    private void OnExamined(Entity<KsFieldGeneratorComponent> entity, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        args.PushMarkup(Loc.GetString("ks-field-generator-examined", ("state", entity.Comp.Enabled)), priority: 4);
    }

    private void OnActivateInWorld(Entity<KsFieldGeneratorComponent> entity, ref ActivateInWorldEvent args)
    {
        if (args.Handled ||
            !args.Complex)
            return;

        SetEnabled(entity!, !entity.Comp.Enabled);

        if (entity.Comp.Enabled)
        {
            if (TryUpdateAnchoredPosition(entity, Transform(entity)))
                GenerateFields(entity);
        }
        else if (entity.Comp.LinkedGeneratorUid is { } linkedUid)
        {
            UnlinkAndClearFields(entity);
        }

        _popupSystem.PopupPredicted(
            Loc.GetString(entity.Comp.Enabled ? "ks-field-generator-enabled" : "ks-field-generator-disabled"),
            entity.Owner,
            args.User,
            Filter.Empty(),
            true
        );

        args.Handled = true;
    }

    private void SetEnabled(Entity<KsFieldGeneratorComponent?> entity, bool value)
    {
        if (!Resolve(entity, ref entity.Comp))
            return;

        entity.Comp.Enabled = value;
        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.Enabled));

        _appearanceSystem.SetData(entity.Owner, KsFieldGeneratorVisuals.State, GetState(entity!));
    }

    private void OnPowerChanged(Entity<KsFieldGeneratorComponent> entity, ref PowerChangedEvent args)
    {
        var wasPowered = entity.Comp.Powered;
        entity.Comp.Powered = args.Powered;

        if (wasPowered && !args.Powered) // Power was lost
        {
            UnlinkAndClearFields(entity);
        }
        else if (!wasPowered && args.Powered && entity.Comp.Enabled) // Should turn back on
        {
            var transformComponent = Transform(entity);
            if (TryUpdateAnchoredPosition(entity, transformComponent) &&
                entity.Comp.LinkedGeneratorUid is not { })
                GenerateFields(entity);
        }

        DirtyField(entity.Owner, entity.Comp, nameof(entity.Comp.Powered));
        _appearanceSystem.SetData(entity.Owner, KsFieldGeneratorVisuals.State, GetState(entity));
    }

    public static KsFieldGeneratorState GetState(KsFieldGeneratorComponent component)
    {
        var state = KsFieldGeneratorState.Off;
        if (component.Enabled)
            state = component.Powered ? KsFieldGeneratorState.On : KsFieldGeneratorState.NeedsPower;

        return state;
    }

    /// <returns>Whether the generator can be/stay enabled, regardless of power.</returns>
    public bool CanGeneratorBeEnabled(Entity<KsFieldGeneratorComponent> entity, TransformComponent? transformComponent = null) =>
        (transformComponent ?? Transform(entity)).Anchored;

    public bool CanGeneratorWork(Entity<KsFieldGeneratorComponent> entity, TransformComponent? transformComponent = null) =>
        entity.Comp.Powered && CanGeneratorBeEnabled(entity, transformComponent: transformComponent);
}
