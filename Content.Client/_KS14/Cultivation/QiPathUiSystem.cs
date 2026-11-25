using Content.Client._KS14.Cultivation.UI;
using Content.Shared._KS14.Cultivation.Components;
using Content.Shared._KS14.Cultivation.Systems;
using Content.Shared.Verbs;
using Robust.Client.Player;
using Robust.Shared.Utility;

namespace Content.Client._KS14.Cultivation;

/// <summary>
///     The "Path of qi" button on a qi cultivator's own examine, and the window it opens: each realm and the techniques
///         it brings.
/// </summary>
public sealed partial class QiPathUiSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedCultivationSystem _cultivationSystem = default!;

    private QiPathWindow? _window;
    private EntityUid? _shown;
    private float _refreshIn;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CultivatorComponent, GetVerbsEvent<ExamineVerb>>(OnExamineVerbs);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
    }

    private void OnExamineVerbs(Entity<CultivatorComponent> entity, ref GetVerbsEvent<ExamineVerb> args)
    {
        if (args.User != entity.Owner)
            return;

        var uid = entity.Owner;
        args.Verbs.Add(new ExamineVerb
        {
            Text = Loc.GetString("qi-path-examine-verb"),
            Message = Loc.GetString("qi-path-examine-verb-message"),
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
            _window = new QiPathWindow();
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
        if (_window == null || _shown is not { } uid || !TryComp<CultivatorComponent>(uid, out var cultivator))
            return;

        _window.Update(cultivator, _cultivationSystem.GetMaxQi(cultivator), _cultivationSystem.GetInsightRequired(cultivator));
    }
}
