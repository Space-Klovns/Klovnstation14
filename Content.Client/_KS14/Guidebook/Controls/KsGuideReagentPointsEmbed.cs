using System.Diagnostics.CodeAnalysis;
using Content.Client.Guidebook.Richtext;
using Content.Client.Message;
using Content.Shared.Chemistry.Reagent;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._KS14.Guidebook.Controls;

/// <summary>
///     Tabulates where reagents boil and melt at one atmosphere on a guidebook page, from their prototypes:
///         <c>&lt;KsGuideReagentPointsEmbed Reagents="KsNaphtha,KsDiesel"/&gt;</c>.
/// </summary>
[UsedImplicitly]
public sealed partial class KsGuideReagentPointsEmbed : BoxContainer, IDocumentTag
{
    private const int ColumnCount = 3;

    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    public KsGuideReagentPointsEmbed()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
    }

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        var sawmill = _logManager.GetSawmill("guidebook.reagent_points");

        if (!args.TryGetValue("Reagents", out var reagents))
        {
            sawmill.Error("Reagent points embed tag is missing its Reagents argument");
            return false;
        }

        var table = new Content.Client.UserInterface.Controls.TableContainer { Columns = ColumnCount, HorizontalExpand = true };
        table.AddChild(MakeCell(Loc.GetString("ks-guidebook-reagent-points-reagent"), header: true));
        table.AddChild(MakeCell(Loc.GetString("ks-guidebook-reagent-points-boils"), header: true));
        table.AddChild(MakeCell(Loc.GetString("ks-guidebook-reagent-points-melts"), header: true));

        foreach (var reagentId in reagents.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!_prototypeManager.TryIndex<ReagentPrototype>(reagentId, out var reagent))
            {
                sawmill.Error($"\"{reagentId}\" is not a reagent prototype");
                return false;
            }

            table.AddChild(MakeCell(reagent.LocalizedName));
            table.AddChild(MakeCell(FormatPoint(reagent.BoilingPoint)));
            table.AddChild(MakeCell(FormatPoint(reagent.MeltingPoint)));
        }

        AddChild(table);
        control = this;
        return true;
    }

    private static string FormatPoint(float? kelvin)
    {
        return kelvin is { } value
            ? Loc.GetString("ks-guidebook-reagent-points-kelvin", ("kelvin", value))
            : Loc.GetString("ks-guidebook-reagent-points-none");
    }

    private static RichTextLabel MakeCell(string text, bool header = false)
    {
        var label = new RichTextLabel();
        label.SetMarkup(header ? $"[bold]{text}[/bold]" : text);
        return label;
    }
}
