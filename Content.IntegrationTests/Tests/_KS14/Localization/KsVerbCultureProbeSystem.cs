#nullable enable
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._KS14.Localization;

/// <summary>
/// Observes culture inside a real network verb callback. Registered before event
/// subscriptions are locked; only the target explicitly chosen by a test opts in.
/// </summary>
public sealed partial class KsVerbCultureProbeSystem : EntitySystem
{
    [Dependency] private ILocalizationManager _localization = default!;

    public EntityUid Target = EntityUid.Invalid;
    public string? ValidationCulture;
    public string? CallbackCulture;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> ev)
    {
        if (ev.Target != Target || !ev.CanAccess)
            return;
        ValidationCulture = _localization.DefaultCulture!.Name;
        ev.Verbs.Add(new Verb
        {
            Text = _localization.GetString("gun-chamber-bolt-close"),
            Act = () => CallbackCulture = _localization.DefaultCulture!.Name,
        });
    }
}
