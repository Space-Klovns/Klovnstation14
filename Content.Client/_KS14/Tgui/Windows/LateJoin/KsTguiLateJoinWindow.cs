using System.Linq;
using System.Numerics;
using Content.Client._KS14.Tgui.Controls;
using Content.Client.CrewManifest;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Client.Players.PlayTimeTracking;
using Content.Client.Stylesheets.Fonts;
using Content.Client.UserInterface.Controls;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.Console;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._KS14.Tgui.Windows.LateJoin;

/// <summary>
///     The late join window laid out after tgui's JobSelection: department cards in their department's colour, a row per
///         job with its icon and open slots, a Random Job button and the shift time. Stations stay selectable (tabs, when
///         there is more than one), as in the stock window.
/// </summary>
public sealed partial class KsTguiLateJoinWindow : FancyWindow
{
    private const int ColumnCount = 3;
    private const int CardGap = 6;

    private static readonly ProtoId<DepartmentPrototype> CommandDepartment = "Command";

    /// <summary>
    ///     tgui's department colours, sampled from its late join screen, by department prototype id. Specific is SS14's
    ///         catch-all, which is tgui's "No Department".
    /// </summary>
    private static readonly Dictionary<string, Color> DepartmentColors = new()
    {
        ["Command"] = Color.FromHex("#FCD203"),
        ["Security"] = Color.FromHex("#D92626"),
        ["Engineering"] = Color.FromHex("#F26C0D"),
        ["Medical"] = Color.FromHex("#00B3B3"),
        ["Science"] = Color.FromHex("#B333CC"),
        ["Cargo"] = Color.FromHex("#A96A3C"),
        ["Civilian"] = Color.FromHex("#1FAD4E"),
        ["Silicon"] = Color.FromHex("#D9268E"),
        ["Specific"] = Color.FromHex("#9689DB"),
    };

    /// <summary>
    ///     The card colour of a department and the colour of the text on it: black on tgui's bright colours.
    /// </summary>
    public static (Color Card, Color Text) DepartmentLook(DepartmentPrototype department)
    {
        if (DepartmentColors.TryGetValue(department.ID, out var known))
            return (known, Color.Black);

        // Another department: its own colour, with whichever of black and white reads better on it.
        var card = department.Color;
        var luminance = 0.2126f * card.R + 0.7152f * card.G + 0.0722f * card.B;
        return (card, luminance > 0.5f ? Color.Black : Color.White);
    }

    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IClientConsoleHost _consoleHost = default!;
    [Dependency] private IConfigurationManager _configManager = default!;
    [Dependency] private IEntitySystemManager _entitySystem = default!;
    [Dependency] private JobRequirementsManager _jobRequirements = default!;
    [Dependency] private IClientPreferencesManager _preferencesManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private KsTguiFonts _fonts = default!;

    private readonly ClientGameTicker _gameTicker;
    private readonly SpriteSystem _sprites;
    private readonly CrewManifestSystem _crewManifest;
    private readonly Action<NetEntity, string> _onSelected;

    private readonly KsTguiSection _section;
    private readonly BoxContainer _body;
    private readonly KsTguiButton _crewManifestButton;
    private readonly List<StationView> _stations = new();

    private int _shownSeconds = -1;

    /// <summary>
    ///     Every job row currently built, for tests.
    /// </summary>
    public IEnumerable<KsTguiJobEntry> JobEntries => _stations.SelectMany(station => station.Entries);

    public KsTguiLateJoinWindow() : this(null)
    {
    }

    /// <param name="onSelected">What picking a job does; null joins the game through the console, as the stock window.</param>
    public KsTguiLateJoinWindow(Action<NetEntity, string>? onSelected)
    {
        IoCManager.InjectDependencies(this);
        _sprites = _entitySystem.GetEntitySystem<SpriteSystem>();
        _crewManifest = _entitySystem.GetEntitySystem<CrewManifestSystem>();
        _gameTicker = _entitySystem.GetEntitySystem<ClientGameTicker>();
        _onSelected = onSelected ?? JoinGame;

        MinSize = new Vector2(520, 360);
        SetSize = new Vector2(1012, 666);
        Title = Loc.GetString("late-join-gui-title");

        _crewManifestButton = new KsTguiButton
        {
            Text = Loc.GetString("crew-manifest-button-label"),
            Visible = _configManager.GetCVar(CCVars.CrewManifestWithoutEntity),
        };
        _crewManifestButton.OnPressed += _ =>
        {
            var station = SelectedStation;
            if (station != null)
                _crewManifest.RequestCrewManifest(station.Id);
        };

        var randomButton = new KsTguiButton
        {
            Text = Loc.GetString("ks-tgui-late-join-random"),
            ToolTip = Loc.GetString("ks-tgui-late-join-random-tooltip"),
        };
        randomButton.OnPressed += _ => SelectRandomJob();
        RandomButton = randomButton;

        var buttons = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        buttons.AddChild(_crewManifestButton);
        buttons.AddChild(randomButton);

        _body = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = CardGap,
        };

        _section = new KsTguiSection
        {
            Fill = true,
            Scrollable = true,
            Buttons = buttons,
        };
        _section.AddChild(_body);
        ContentsContainer.AddChild(_section);

        RefreshShiftTime();
        _jobRequirements.Updated += RebuildUI;
        _gameTicker.LobbyJobsAvailableUpdated += JobsAvailableUpdated;
        RebuildUI();
    }

    public KsTguiButton RandomButton { get; }

    private StationView? SelectedStation => _stations.FirstOrDefault(station => station.Root.Visible);

    private void JoinGame(NetEntity station, string jobId)
    {
        _consoleHost.ExecuteCommand($"joingame {CommandParsing.Escape(jobId)} {station}");
        Close();
    }

    /// <summary>
    ///     tgui's Random Job: any job on the shown station that can be taken.
    /// </summary>
    public void SelectRandomJob()
    {
        var station = SelectedStation;
        if (station == null)
            return;

        var candidates = station.Entries.Where(entry => !entry.Disabled).ToList();
        if (candidates.Count == 0)
            return;

        var pick = _random.Pick(candidates);
        _onSelected(station.Id, pick.JobId);
    }

    private void RebuildUI()
    {
        Populate(_gameTicker.StationNames, _gameTicker.JobsAvailable);
    }

    public void Populate(
        IReadOnlyDictionary<NetEntity, string> stationNames,
        IReadOnlyDictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>> jobsAvailable)
    {
        var shownId = SelectedStation?.Id;

        _body.RemoveAllChildren();
        _stations.Clear();

        var departments = _prototypeManager.EnumeratePrototypes<DepartmentPrototype>().ToArray();
        Array.Sort(departments, DepartmentUIComparer.Instance);

        foreach (var (id, name) in stationNames)
        {
            if (!jobsAvailable.TryGetValue(id, out var available))
                available = new Dictionary<ProtoId<JobPrototype>, int?>();

            _stations.Add(BuildStation(id, name, available, departments));
        }

        // Stay on the station that was shown, so a slot change does not throw the player back to the first.
        var shown = _stations.FirstOrDefault(station => station.Id == shownId) ?? _stations.FirstOrDefault();
        foreach (var station in _stations)
            station.Root.Visible = station == shown;

        if (_stations.Count > 1)
        {
            var tabs = new KsTguiTabs();
            foreach (var station in _stations)
                station.Tab = tabs.AddTab(station.Name, selected: station == shown);

            tabs.OnTabSelected += item =>
            {
                foreach (var station in _stations)
                    station.Root.Visible = station.Tab == item;
            };

            _body.AddChild(tabs);
        }

        foreach (var station in _stations)
            _body.AddChild(station.Root);
    }

    private StationView BuildStation(
        NetEntity id,
        string name,
        Dictionary<ProtoId<JobPrototype>, int?> available,
        DepartmentPrototype[] departments)
    {
        var view = new StationView(id, name);
        var cards = new List<(Control Card, int Height)>();

        foreach (var department in departments)
        {
            var jobs = new List<JobPrototype>();
            foreach (var jobId in department.Roles)
            {
                if (available.ContainsKey(jobId))
                    jobs.Add(_prototypeManager.Index(jobId));
            }

            // Do not display departments with no jobs available.
            if (jobs.Count == 0)
                continue;

            jobs.Sort(JobUIComparer.Instance);
            cards.Add((BuildCard(id, department, jobs, available, view), jobs.Count + 2));
        }

        // Fill the columns in order, as CSS columns do: the next one starts once the share is used up.
        var columns = new List<BoxContainer>();
        var share = cards.Sum(card => card.Height) / (float)ColumnCount;
        var filled = 0f;
        BoxContainer? column = null;

        foreach (var (card, height) in cards)
        {
            if (column == null || (filled >= share - 0.5f && columns.Count < ColumnCount))
            {
                column = new BoxContainer
                {
                    Orientation = LayoutOrientation.Vertical,
                    HorizontalExpand = true,
                    SeparationOverride = CardGap,
                };
                columns.Add(column);
                filled = 0f;
            }

            column.AddChild(card);
            filled += (float)height;
        }

        // Always three columns wide, so a short list does not stretch its cards across the window.
        while (columns.Count < ColumnCount)
            columns.Add(new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true });

        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = CardGap,
            HorizontalExpand = true,
        };

        foreach (var columnBox in columns)
            row.AddChild(columnBox);

        view.Root = row;
        return view;
    }

    private Control BuildCard(
        NetEntity stationId,
        DepartmentPrototype department,
        List<JobPrototype> jobs,
        Dictionary<ProtoId<JobPrototype>, int?> available,
        StationView view)
    {
        var (color, textColor) = DepartmentLook(department);
        var titleLabel = new Label
        {
            Text = Loc.GetString(department.Name),
            FontOverride = _fonts.GetFont(15, FontKind.Bold),
            FontColorOverride = textColor,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            ClipText = true,
        };

        var slotsLabel = new Label
        {
            FontOverride = _fonts.GetFont(13),
            FontColorOverride = textColor,
            VerticalAlignment = VAlignment.Center,
        };

        var titleRow = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        titleRow.AddChild(titleLabel);
        titleRow.AddChild(slotsLabel);

        var title = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = color,
                BorderColor = Darken(color, 50f),
                BorderThickness = new Thickness(0f, 0f, 0f, 2f),
                ContentMarginLeftOverride = 6f,
                ContentMarginRightOverride = 6f,
                ContentMarginTopOverride = 4f,
                ContentMarginBottomOverride = 4f,
            },
        };
        title.AddChild(titleRow);

        var list = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            SeparationOverride = 4,
            Margin = new Thickness(6f, 6f, 6f, 6f),
        };

        var entries = new List<KsTguiJobEntry>();
        var profile = (HumanoidCharacterProfile?)_preferencesManager.Preferences?.SelectedCharacter;
        var isCommand = department.ID == CommandDepartment.Id;

        void RefreshSlots()
        {
            slotsLabel.Text = SlotsText(entries.Select(entry => entry.Amount));
        }

        foreach (var job in jobs)
        {
            var icon = _sprites.Frame0(_prototypeManager.Index(job.Icon).Icon);
            var entry = new KsTguiJobEntry(job.ID, job.LocalizedName, color, textColor, isCommand, icon, _fonts);
            entry.SetAmount(available[job.ID]);

            if (!_jobRequirements.IsAllowed(job, profile, out var reason))
                entry.Lock(reason);
            else if (!string.IsNullOrEmpty(job.LocalizedDescription))
                entry.SetDescription(job.LocalizedDescription);

            entry.OnPressed += _ => _onSelected(stationId, entry.JobId);
            entry.AmountChanged += RefreshSlots;
            entries.Add(entry);
            list.AddChild(entry);
        }

        RefreshSlots();
        view.Entries.AddRange(entries);

        var card = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = color },
            ToolTip = Loc.GetString("late-join-gui-jobs-amount-in-department-tooltip",
                ("departmentName", Loc.GetString(department.Name))),
        };

        var box = new BoxContainer { Orientation = LayoutOrientation.Vertical };
        box.AddChild(title);
        box.AddChild(list);
        card.AddChild(box);
        return card;
    }

    private static string SlotsText(IEnumerable<int?> amounts)
    {
        var total = 0;
        foreach (var amount in amounts)
        {
            if (amount == null)
                return Loc.GetString("ks-tgui-late-join-slots-unlimited");

            total += amount.Value;
        }

        return Loc.GetString("ks-tgui-late-join-slots", ("count", total));
    }

    /// <summary>
    ///     tgui-core's <c>Color.darken</c>: each channel scaled down by the percentage.
    /// </summary>
    public static Color Darken(Color color, float percent)
    {
        var scale = 1f - percent / 100f;
        return new Color(color.R * scale, color.G * scale, color.B * scale, color.A);
    }

    private void JobsAvailableUpdated(IReadOnlyDictionary<NetEntity, Dictionary<ProtoId<JobPrototype>, int?>> updatedJobs)
    {
        foreach (var station in _stations)
        {
            if (!updatedJobs.TryGetValue(station.Id, out var jobs))
                continue;

            foreach (var entry in station.Entries)
            {
                if (jobs.TryGetValue(entry.JobId, out var amount))
                    entry.SetAmount(amount);
            }
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        RefreshShiftTime();
    }

    private void RefreshShiftTime()
    {
        var duration = _gameTicker.RoundDuration();
        var seconds = (int)duration.TotalSeconds;
        if (seconds == _shownSeconds)
            return;

        _shownSeconds = seconds;
        _section.Title = Loc.GetString("ks-tgui-late-join-shift-time",
            ("time", duration.ToString(duration.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss")));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        _jobRequirements.Updated -= RebuildUI;
        _gameTicker.LobbyJobsAvailableUpdated -= JobsAvailableUpdated;
        _stations.Clear();
    }

    private sealed class StationView
    {
        public readonly NetEntity Id;
        public readonly string Name;
        public readonly List<KsTguiJobEntry> Entries = new();
        public Control Root = default!;
        public KsTguiTabItem? Tab;

        public StationView(NetEntity id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}

/// <summary>
///     One job in a department card: tgui's fluid job button, in the department's colour, with the job's icon, name (bold
///         for command jobs) and open slots.
/// </summary>
public sealed class KsTguiJobEntry : ContainerButton
{
    private static readonly Color UnavailableBackground = Color.FromHex("#949494");
    private static readonly Color UnavailableText = Color.FromHex("#616161");

    private readonly Color _text;
    private readonly Label _nameLabel;
    private readonly Label _slotsLabel;
    private readonly BoxContainer _row;
    private readonly StyleBoxFlat _normalBox;
    private readonly StyleBoxFlat _hoveredBox;
    private readonly StyleBoxFlat _disabledBox;

    private string? _tooltipText;
    private bool _locked;

    public string JobId { get; }
    public int? Amount { get; private set; }

    public event Action? AmountChanged;

    public KsTguiJobEntry(
        string jobId,
        string jobName,
        Color departmentColor,
        Color textColor,
        bool command,
        Texture? icon,
        KsTguiFonts fonts)
    {
        JobId = jobId;
        _text = textColor;

        _normalBox = MakeBox(KsTguiLateJoinWindow.Darken(departmentColor, 10f));
        _hoveredBox = MakeBox(departmentColor);
        _disabledBox = MakeBox(UnavailableBackground);
        StyleBoxOverride = _normalBox;
        HorizontalExpand = true;

        _row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
        if (icon != null)
        {
            _row.AddChild(new TextureRect
            {
                Texture = icon,
                TextureScale = new Vector2(2f, 2f),
                VerticalAlignment = VAlignment.Center,
                Margin = new Thickness(0f, 0f, 4f, 0f),
            });
        }

        _nameLabel = new Label
        {
            Text = jobName,
            FontOverride = fonts.GetFont(13, command ? FontKind.Bold : FontKind.Regular),
            FontColorOverride = _text,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            ClipText = true,
        };
        _row.AddChild(_nameLabel);

        _slotsLabel = new Label
        {
            FontOverride = fonts.GetFont(13),
            FontColorOverride = _text,
            VerticalAlignment = VAlignment.Center,
        };
        _row.AddChild(_slotsLabel);
        AddChild(_row);

        TooltipSupplier = _ => _tooltipText == null ? null : MakeTooltip(_tooltipText);
    }

    private static StyleBoxFlat MakeBox(Color color)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = color,
            ContentMarginLeftOverride = 6f,
            ContentMarginRightOverride = 6f,
            ContentMarginTopOverride = 3f,
            ContentMarginBottomOverride = 3f,
        };
    }

    private static Control MakeTooltip(string text)
    {
        var tooltip = new Tooltip();
        tooltip.SetMessage(FormattedMessage.FromUnformatted(text));
        return tooltip;
    }

    public void SetAmount(int? amount)
    {
        Amount = amount;
        _slotsLabel.Text = amount?.ToString() ?? Loc.GetString("ks-tgui-late-join-unlimited");
        RefreshAvailability();
        AmountChanged?.Invoke();
    }

    public void SetDescription(string description)
    {
        _tooltipText = description;
    }

    /// <summary>
    ///     The job can't be taken by this player; the reason shows as its tooltip.
    /// </summary>
    public void Lock(FormattedMessage reason)
    {
        _locked = true;
        _tooltipText = reason.IsEmpty ? null : reason.ToString();

        _row.AddChild(new KsTguiIcon
        {
            Glyph = KsTguiIcons.Lock,
            IconColor = UnavailableText,
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(4f, 0f, 0f, 0f),
        });

        RefreshAvailability();
    }

    private void RefreshAvailability()
    {
        Disabled = _locked || Amount == 0;
    }

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();

        // The base constructor calls this before the fields exist.
        if (_nameLabel == null)
            return;

        StyleBoxOverride = DrawMode switch
        {
            DrawModeEnum.Disabled => _disabledBox,
            DrawModeEnum.Hover or DrawModeEnum.Pressed => _hoveredBox,
            _ => _normalBox,
        };

        var text = DrawMode == DrawModeEnum.Disabled ? UnavailableText : _text;
        _nameLabel.FontColorOverride = text;
        _slotsLabel.FontColorOverride = text;
    }
}
