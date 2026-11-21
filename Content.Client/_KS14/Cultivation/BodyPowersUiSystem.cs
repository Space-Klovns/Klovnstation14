using Content.Client._KS14.Cultivation.UI;
using Content.Shared._KS14.Cultivation.Body;
using Content.Shared.Verbs;
using Robust.Client.Player;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Cultivation;

/// <summary>
///     The "Body powers" button on a body cultivator's own examine, and the window it opens: every body part with its
///         level and what it has unlocked.
/// </summary>
public sealed partial class BodyPowersUiSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;

    private BodyPowersWindow? _window;
    private EntityUid? _shown;
    private float _refreshIn;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BodyCultivatorComponent, GetVerbsEvent<ExamineVerb>>(OnExamineVerbs);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
    }

    private void OnExamineVerbs(Entity<BodyCultivatorComponent> entity, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (args.User != entity.Owner || !entity.Comp.Committed)
            return;

        var uid = entity.Owner;
        args.Verbs.Add(new ExamineVerb
        {
            Text = Loc.GetString("body-powers-examine-verb"),
            Message = Loc.GetString("body-powers-examine-verb-message"),
            Category = VerbCategory.Examine,
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png")),
            ShowOnExamineTooltip = true,
            ClientExclusive = true,
            Act = () => Open(uid),
        });
    }

    public void Open(EntityUid uid)
    {
        if (_window == null)
        {
            _window = new BodyPowersWindow();
            _window.OnClose += () =>
            {
                _window = null;
                _shown = null;
            };
        }

        _shown = uid;
        Refresh();
        _window.OpenCentered();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (_window == null)
            return;

        // Tempering and levels change as you train; keep the window current without rebuilding every frame.
        _refreshIn -= frameTime;
        if (_refreshIn > 0f)
            return;

        _refreshIn = 0.5f;
        if (_shown != _playerManager.LocalEntity)
        {
            _window.Close();
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_window == null || _shown is not { } uid || !TryComp<BodyCultivatorComponent>(uid, out var bodyComponent))
            return;

        _window.Update(bodyComponent);
    }
}
