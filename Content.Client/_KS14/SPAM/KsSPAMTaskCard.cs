using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared._KS14.SPAM;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._KS14.SPAM;

/// <summary>
///     One task on the S.P.A.M. screen: its picture on a plaque, and on another its name, reward, progress and time.
///         Pressing it opens the details. A tracked task is outlined.
/// </summary>
public sealed class KsSPAMTaskCard : ContainerButton
{
    /// <summary>
    ///     Every card is this wide, so the row scrolls evenly.
    /// </summary>
    public const float CardWidth = 156f;

    private const float TextWidth = CardWidth - 28f;

    private static readonly Color PlaqueColor = Color.Black.WithAlpha(0.35f);
    private static readonly Color TrackedColor = Color.FromHex("#5BA85B");

    // Not Color.Transparent, which is white.
    private static readonly Color Clear = Color.Black.WithAlpha(0f);

    private readonly StyleBoxFlat _outline = new()
    {
        BackgroundColor = Clear,
        BorderColor = Clear,
        BorderThickness = new Thickness(2),
    };

    private readonly EntityPrototypeView _iconView;
    private readonly RichTextLabel _nameLabel;
    private readonly Label _rewardLabel;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressLabel;
    private readonly Label _timeLabel;

    private TimeSpan? _endTime;
    private int _shownSeconds = -1;

    public ProtoId<KsSPAMTaskPrototype> Task { get; private set; }

    public bool Tracked { get; private set; }

    public KsSPAMTaskCard()
    {
        SetWidth = CardWidth;
        AddStyleClass(StyleClass.ButtonSquare);

        _iconView = new EntityPrototypeView
        {
            SetSize = new Vector2(64, 64),
            Scale = new Vector2(2, 2),
            HorizontalAlignment = HAlignment.Center,
        };
        var iconPlaque = MakePlaque(_iconView);

        // Wraps: task names run long, and longer still in Russian. Two lines are kept for it so the cards line up.
        _nameLabel = new RichTextLabel { MaxWidth = TextWidth, MinHeight = 36, HorizontalAlignment = HAlignment.Center };
        _rewardLabel = new Label { HorizontalAlignment = HAlignment.Center, StyleClasses = { StyleClass.LabelWeak } };

        _progressBar = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 18 };
        _progressLabel = new Label { HorizontalAlignment = HAlignment.Center, VerticalAlignment = VAlignment.Center };
        var progress = new Control();
        progress.AddChild(_progressBar);
        progress.AddChild(_progressLabel);

        _timeLabel = new Label { HorizontalAlignment = HAlignment.Center, StyleClasses = { StyleClass.LabelWeak } };

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
        };
        text.AddChild(_nameLabel);
        text.AddChild(_rewardLabel);
        text.AddChild(progress);
        text.AddChild(_timeLabel);

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(4),
        };
        box.AddChild(iconPlaque);
        box.AddChild(MakePlaque(text));

        var outline = new PanelContainer { PanelOverride = _outline };
        outline.AddChild(box);
        AddChild(outline);
    }

    /// <summary>
    ///     A dark panel behind part of a task, so it reads as a label set into the card.
    /// </summary>
    public static PanelContainer MakePlaque(Control content)
    {
        var plaque = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = PlaqueColor,
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 4,
                ContentMarginBottomOverride = 4,
            },
        };
        plaque.AddChild(content);
        return plaque;
    }

    public void SetTask(KsSPAMActiveTask task, IPrototypeManager prototypeManager)
    {
        Task = task.Task;
        Tracked = task.Tracked;
        _endTime = task.EndTime;
        _shownSeconds = -1;
        _outline.BorderColor = task.Tracked ? TrackedColor : Clear;
        _progressBar.Value = task.Amount > 0 ? task.Progress / (float)task.Amount : 0f;
        _progressLabel.Text = Loc.GetString("ks-spam-task-progress", ("progress", task.Progress), ("amount", task.Amount));

        if (!prototypeManager.TryIndex(task.Task, out var taskPrototype))
            return;

        _nameLabel.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(taskPrototype.Name)));
        _iconView.SetPrototype(taskPrototype.Icon);
        _rewardLabel.Text = Loc.GetString("ks-spam-task-reward",
            ("points", taskPrototype.PointsPerUnit * task.Amount),
            ("reputation", taskPrototype.Reputation));

        // The time only runs once the task is tracked. Until then the card says how long it would get.
        if (task.EndTime == null)
            _timeLabel.Text = Loc.GetString("ks-spam-task-duration", ("time", KsSPAMTaskWindow.FormatTime(taskPrototype.Duration)));
    }

    /// <summary>
    ///     Counts the time left down. Called every frame, so it only rewrites the label when the second changes.
    /// </summary>
    public void UpdateTime(TimeSpan curTime)
    {
        if (_endTime is not { } endTime)
            return;

        var timeLeft = endTime - curTime;
        var seconds = Math.Max((int)timeLeft.TotalSeconds, 0);
        if (seconds == _shownSeconds)
            return;

        _shownSeconds = seconds;
        _timeLabel.Text = Loc.GetString("ks-spam-task-time-left", ("time", KsSPAMTaskWindow.FormatTime(timeLeft)));
    }
}
